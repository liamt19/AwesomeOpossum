
#include <immintrin.h>
#include <xmmintrin.h>

#include "defs.h"
#include "simd.h"

#if defined(_MSC_VER)
#define DLL_EXPORT extern "C" __declspec(dllexport)
#else
#define DLL_EXPORT extern "C"
#endif


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


template<i32 L1_SIZE>
i32 ValueEvaluateImpl(const i16* us, const i16* them, const i16* l1w, const i16 l1b) {
    
    constexpr auto VALUE_QA = 256;
    constexpr auto VALUE_QB = 64;

    constexpr auto SIMD_CHUNKS = L1_SIZE / (sizeof(vec_i16) / sizeof(i16));

    vec_i32 sum = vec_setzero_epi32();
    const auto zero = vec_set1_epi16(0);
    const auto one = vec_set1_epi16(VALUE_QA);

    const auto stmData = reinterpret_cast<const vec_i16*>(us);
    const auto ntmData = reinterpret_cast<const vec_i16*>(them);

    const auto stmWeights = reinterpret_cast<const vec_i16*>(&l1w[0]);
    const auto ntmWeights = reinterpret_cast<const vec_i16*>(&l1w[L1_SIZE]);

    for (i32 i = 0; i < SIMD_CHUNKS; i += 2) {
        const auto v0 = vec_min_epi16(one, vec_max_epi16(stmData[i + 0], zero));
        const auto v1 = vec_min_epi16(one, vec_max_epi16(stmData[i + 1], zero));

        const auto m0 = vec_mullo_epi16(v0, stmWeights[i + 0]);
        const auto m1 = vec_mullo_epi16(v1, stmWeights[i + 1]);

        const auto s0 = vec_madd_epi16(m0, v0);
        const auto s1 = vec_madd_epi16(m1, v1);

        sum = vec_add_epi32(sum, vec_add_epi32(s0, s1));
    }

    for (i32 i = 0; i < SIMD_CHUNKS; i += 2) {
        const auto v0 = vec_min_epi16(one, vec_max_epi16(ntmData[i + 0], zero));
        const auto v1 = vec_min_epi16(one, vec_max_epi16(ntmData[i + 1], zero));

        const auto m0 = vec_mullo_epi16(v0, ntmWeights[i + 0]);
        const auto m1 = vec_mullo_epi16(v1, ntmWeights[i + 1]);

        const auto s0 = vec_madd_epi16(m0, v0);
        const auto s1 = vec_madd_epi16(m1, v1);

        sum = vec_add_epi32(sum, vec_add_epi32(s0, s1));
    }

    i32 output = vec_hsum_8x32(sum);
    return (((output / VALUE_QA) + l1b) * 400) / (VALUE_QA * VALUE_QB);
}


#define EXP_VAL(N) \
    DLL_EXPORT i32 ValueEvaluate##N(const i16* us, const i16* them, const i16* l1w, const i16 l1b) { return ValueEvaluateImpl<N>(us, them, l1w, l1b); }

EXP_VAL(  64)
EXP_VAL( 128)
EXP_VAL( 256)
EXP_VAL( 512)
EXP_VAL( 768)
EXP_VAL(1024)
EXP_VAL(1280)
EXP_VAL(1536)
EXP_VAL(1792)
EXP_VAL(2048)

