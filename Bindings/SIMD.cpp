
#include <immintrin.h>
#include <xmmintrin.h>

#include "defs.h"
#include "simd.h"

#if defined(_MSC_VER)
#define DLL_EXPORT extern "C" __declspec(dllexport)
#else
#define DLL_EXPORT extern "C"
#endif

constexpr auto POLICY_L1_SIZE = 512;

constexpr auto VALUE_L1_SIZE = 512;
constexpr auto VALUE_QA = 255;
constexpr auto VALUE_QB = 64;

DLL_EXPORT i32 PolicyEvaluate(const i16* us, const i16* them, const i16* L1Weights) {

    vec_i32 sum = vec_setzero_epi32();

    const auto Stride = (POLICY_L1_SIZE / (sizeof(__m256i) / sizeof(i16))) / 2;

    auto data0 = reinterpret_cast<const __m256i*>(&us[0]);
    auto data1 = &data0[Stride];
    auto weights = reinterpret_cast<const __m256i*>(&L1Weights[0]);
    for (i32 i = 0; i < Stride; i++) {
        const auto m0 = _mm256_mullo_epi16(data0[i], weights[i]);
        const auto m1 = _mm256_madd_epi16(data1[i], m0);
        sum = _mm256_add_epi32(sum, m1);
    }

    data0 = reinterpret_cast<const __m256i*>(&them[0]);
    data1 = &data0[Stride];
    weights = reinterpret_cast<const __m256i*>(&L1Weights[POLICY_L1_SIZE / 2]);
    for (i32 i = 0; i < Stride; i++) {
        const auto m0 = _mm256_mullo_epi16(data0[i], weights[i]);
        const auto m1 = _mm256_madd_epi16(data1[i], m0);
        sum = _mm256_add_epi32(sum, m1);
    }

    i32 output = vec_hsum_8x32(sum);
    return output;
}



DLL_EXPORT i32 ValueEvaluate(const i16* us, const i16* them, const i16* L1Weights, const i16 L1Bias) {

    vec_i32 sum = vec_setzero_epi32();
    const auto zero = vec_set1_epi16(0);
    const auto one = vec_set1_epi16(VALUE_QA);

    const auto stmData = reinterpret_cast<const vec_i16*>(us);
    const auto ntmData = reinterpret_cast<const vec_i16*>(them);

    const auto stmWeights = reinterpret_cast<const vec_i16*>(&L1Weights[0]);
    const auto ntmWeights = reinterpret_cast<const vec_i16*>(&L1Weights[VALUE_L1_SIZE]);

    constexpr auto SIMD_CHUNKS = VALUE_L1_SIZE / (sizeof(vec_i16) / sizeof(i16));

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
    return (((output / VALUE_QA) + L1Bias) * 400) / (VALUE_QA * VALUE_QB);
}
