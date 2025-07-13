
#include <immintrin.h>
#include <xmmintrin.h>
#include <iostream>
#include <array>
#include <span>
#include <bit>

#include "defs.h"
#include "simd.h"

#if defined(_MSC_VER)
#define DLL_EXPORT extern "C" __declspec(dllexport)
#else
#define DLL_EXPORT extern "C"
#endif


struct NNZTable {
    __m128i Entries[256];
};
NNZTable nnzTable;

DLL_EXPORT void SetupNNZ() {
    for (u32 i = 0; i < 256; i++) {
        u16* ptr = reinterpret_cast<u16*>(&nnzTable.Entries[i]);

        u32 j = i;
        u32 k = 0;
        while (j != 0) {
            u32 lsbIndex = std::countr_zero(j);
            j &= j - 1;
            ptr[k++] = (u16)lsbIndex;
        }
    }
}


DLL_EXPORT i32 PolicyEvaluate(const i16* us, const i16* them, const i16* l1w) {

    constexpr auto POLICY_L1_SIZE = 512;
    const auto Stride = (POLICY_L1_SIZE / (sizeof(__m256i) / sizeof(i16))) / 2;

    vec_i32 sum = vec_setzero_epi32();

    auto data0 = reinterpret_cast<const __m256i*>(&us[0]);
    auto data1 = &data0[Stride];
    auto weights = reinterpret_cast<const __m256i*>(&l1w[0]);
    for (i32 i = 0; i < Stride; i++) {
        const auto m0 = _mm256_mullo_epi16(data0[i], weights[i]);
        const auto m1 = _mm256_madd_epi16(data1[i], m0);
        sum = _mm256_add_epi32(sum, m1);
    }

    data0 = reinterpret_cast<const __m256i*>(&them[0]);
    data1 = &data0[Stride];
    weights = reinterpret_cast<const __m256i*>(&l1w[POLICY_L1_SIZE / 2]);
    for (i32 i = 0; i < Stride; i++) {
        const auto m0 = _mm256_mullo_epi16(data0[i], weights[i]);
        const auto m1 = _mm256_madd_epi16(data1[i], m0);
        sum = _mm256_add_epi32(sum, m1);
    }

    i32 output = vec_hsum_8x32(sum);
    return output;
}


template<i32 L1_SIZE, i32 L2_SIZE, i32 L3_SIZE>
f32 ValueEvaluateImpl(const i16* us, const i16* them, 
                      const i8* L1Weights, const f32* L1Biases, 
                      const f32* L2Weights, const f32* L2Biases, 
                      const f32* L3Weights, const f32 L3Bias) {
    
    constexpr auto FT_QUANT = 256;
    constexpr auto L1_QUANT = 64;
    constexpr auto FT_SHIFT = 10;
    constexpr f32 L1_MUL = (1 << FT_SHIFT) / static_cast<float>(FT_QUANT * FT_QUANT * L1_QUANT);

    constexpr auto L1_PAIR_COUNT = L1_SIZE / 2;

    i32 nnzCount = 0;
    alignas(32) u16 nnzIndices[L1_SIZE / L1_CHUNK_PER_32];
    alignas(32) i8 FTOutputs[L1_SIZE];

    alignas(32) vec_i32 L1Temp[L2_SIZE / I32_CHUNK_SIZE] = {};
    alignas(32) f32 L1Outputs[L2_SIZE];

    alignas(32) vec_ps L2Outputs[L3_SIZE / F32_CHUNK_SIZE];

    //  FT
    {
        const auto ft_zero = vec_setzero_epi16();
        const auto ft_one = vec_set1_epi16(FT_QUANT);
        const vec_128i baseInc = _mm_set1_epi16(u16(8));
        vec_128i baseVec = _mm_setzero_si128();
        i32 offset = 0;

        for (const auto acc : { us, them }) {
            for (i32 i = 0; i < L1_PAIR_COUNT; i += (I16_CHUNK_SIZE * 2)) {
                const auto input0a = vec_load_epi16(reinterpret_cast<const vec_i16*>(&acc[i + 0 * I16_CHUNK_SIZE + 0]));
                const auto input0b = vec_load_epi16(reinterpret_cast<const vec_i16*>(&acc[i + 1 * I16_CHUNK_SIZE + 0]));

                const auto input1a = vec_load_epi16(reinterpret_cast<const vec_i16*>(&acc[i + 0 * I16_CHUNK_SIZE + L1_PAIR_COUNT]));
                const auto input1b = vec_load_epi16(reinterpret_cast<const vec_i16*>(&acc[i + 1 * I16_CHUNK_SIZE + L1_PAIR_COUNT]));

                const auto clipped0a = vec_min_epi16(vec_max_epi16(input0a, ft_zero), ft_one);
                const auto clipped0b = vec_min_epi16(vec_max_epi16(input0b, ft_zero), ft_one);

                const auto clipped1a = vec_min_epi16(input1a, ft_one);
                const auto clipped1b = vec_min_epi16(input1b, ft_one);

                const auto producta = vec_mulhi_epi16(vec_slli_epi16(clipped0a, 16 - FT_SHIFT), clipped1a);
                const auto productb = vec_mulhi_epi16(vec_slli_epi16(clipped0b, 16 - FT_SHIFT), clipped1b);

                const auto product_one = vec_packus_epi16(producta, productb);
                vec_storeu_epi8(reinterpret_cast<vec_i8*>(&FTOutputs[offset + i]), product_one);

                const auto nnz_mask = vec_nnz_mask(product_one);

                for (i32 j = 0; j < NNZ_OUTPUTS_PER_CHUNK; j++) {
                    i32 lookup = (nnz_mask >> (j * 8)) & 0xFF;
                    auto offsets = nnzTable.Entries[lookup];
                    _mm_storeu_si128(reinterpret_cast<vec_128i*>(&nnzIndices[nnzCount]), _mm_add_epi16(baseVec, offsets));

                    nnzCount += std::popcount(static_cast<u32>(lookup));
                    baseVec = _mm_add_epi16(baseVec, baseInc);
                }

            }

            offset += L1_PAIR_COUNT;
        }
    }


    //  L1
    {
        i8* L1Inputs = FTOutputs;
        const auto inputs32 = (i32*)(FTOutputs);
        for (i32 i = 0; i < nnzCount; i++) {
            const auto index = nnzIndices[i];
            const auto input32 = vec_set1_epi32(inputs32[index]);
            const auto weight = reinterpret_cast<const vec_i8*>(&L1Weights[index * L1_CHUNK_PER_32 * L2_SIZE]);
            for (i32 k = 0; k < L2_SIZE / F32_CHUNK_SIZE; k++)
                L1Temp[k] = vec_dpbusd_epi32(L1Temp[k], input32, weight[k]);
        }

        const auto zero = vec_set1_ps(0.0f);
        const auto one = vec_set1_ps(1.0f);
        const auto sumMul = vec_set1_ps(L1_MUL);
        for (i32 i = 0; i < L2_SIZE / F32_CHUNK_SIZE; ++i) {
            const auto biasVec = vec_loadu_ps(&L1Biases[i * F32_CHUNK_SIZE]);
            const auto sumPs = vec_fmadd_ps(vec_cvtepi32_ps(L1Temp[i]), sumMul, biasVec);
            const auto clipped = vec_min_ps(vec_max_ps(sumPs, zero), one);
            const auto squared = vec_mul_ps(clipped, clipped);
            vec_storeu_ps(&L1Outputs[i * F32_CHUNK_SIZE], squared);
        }
    }


    //  L2
    {
        float* L2Inputs = L1Outputs;
        for (i32 i = 0; i < L3_SIZE / F32_CHUNK_SIZE; ++i)
            L2Outputs[i] = vec_loadu_ps(&L2Biases[i * F32_CHUNK_SIZE]);

        for (i32 i = 0; i < L2_SIZE; ++i) {
            const auto inputVec = vec_set1_ps(L2Inputs[i]);
            const auto weight = reinterpret_cast<const vec_ps*>(&L2Weights[i * L3_SIZE]);
            for (i32 j = 0; j < L3_SIZE / F32_CHUNK_SIZE; ++j)
                L2Outputs[j] = vec_fmadd_ps(inputVec, weight[j], L2Outputs[j]);
        }
    }


    //  L3
    {
        auto l3Sum = vec_set1_ps(0.0f);
        const auto zero = vec_set1_ps(0.0f);
        const auto one = vec_set1_ps(1.0f);
        for (i32 i = 0; i < L3_SIZE / F32_CHUNK_SIZE; ++i) {
            const auto clipped = vec_min_ps(vec_max_ps(L2Outputs[i], zero), one);
            const auto squared = vec_mul_ps(clipped, clipped);

            const auto weightVec = vec_loadu_ps(&L3Weights[i * F32_CHUNK_SIZE]);
            l3Sum = vec_fmadd_ps(squared, weightVec, l3Sum);
        }

        return (L3Bias + vec_hsum_ps(l3Sum)) * 400;
    }
}

#define EXP_VAL(N, O, P) EXP_VAL_IMPL(N, O, P)
#define EXP_VAL_IMPL(N, O, P) \
    DLL_EXPORT f32 ValueEvaluate##N##_##O##_##P(const i16* us, const i16* them, const i8* l1w, const f32* l1b, const f32* l2w, const f32* l2b, const f32* l3w, const f32 l3b) { return ValueEvaluateImpl<N, O, P>(us, them, l1w, l1b, l2w, l2b, l3w, l3b); }

EXP_VAL(  64, 64, 256)
EXP_VAL( 128, 64, 256)
EXP_VAL( 256, 64, 256)
EXP_VAL( 512, 64, 256)
EXP_VAL( 768, 64, 256)
EXP_VAL(1024, 64, 256)
EXP_VAL(1280, 64, 256)
EXP_VAL(1536, 64, 256)
EXP_VAL(1792, 64, 256)
EXP_VAL(2048, 64, 256)

