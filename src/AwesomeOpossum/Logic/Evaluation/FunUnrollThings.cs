
using System.Runtime.CompilerServices;

#if AVX512
using VectorT = System.Runtime.Intrinsics.Vector512;
using VShort = System.Runtime.Intrinsics.Vector512<short>;
#else
using VShort = System.Runtime.Intrinsics.Vector256<short>;
#endif

#pragma warning disable CS0162 // Unreachable code detected

namespace AwesomeOpossum.Logic.Evaluation;

public static unsafe class ValueUnrollThings
{

#if AVX512
    private const int N = 32;
#else
    private const int N = 16;
#endif

    private const int HL = ValueNetwork.L1_SIZE;
    private const int StopBefore = HL / N;


    [MethodImpl(Inline)]
    public static void Add(short* _src, short* _dst, short* _add1)
    {
        VShort* src  = (VShort*)_src;
        VShort* dst  = (VShort*)_dst;
        VShort* add1 = (VShort*)_add1;

        dst[ 0] = src[ 0] + add1[ 0];

        if (StopBefore == 1) return;

        dst[ 1] = src[ 1] + add1[ 1];

        if (StopBefore == 2) return;

        dst[ 2] = src[ 2] + add1[ 2];
        dst[ 3] = src[ 3] + add1[ 3];

        if (StopBefore == 4) return;

        dst[ 4] = src[ 4] + add1[ 4];
        dst[ 5] = src[ 5] + add1[ 5];
        dst[ 6] = src[ 6] + add1[ 6];
        dst[ 7] = src[ 7] + add1[ 7];

        if (StopBefore == 8) return;

        dst[ 8] = src[ 8] + add1[ 8];
        dst[ 9] = src[ 9] + add1[ 9];
        dst[10] = src[10] + add1[10];
        dst[11] = src[11] + add1[11];
        dst[12] = src[12] + add1[12];
        dst[13] = src[13] + add1[13];
        dst[14] = src[14] + add1[14];
        dst[15] = src[15] + add1[15];

        if (StopBefore == 16) return;

        dst[16] = src[16] + add1[16];
        dst[17] = src[17] + add1[17];
        dst[18] = src[18] + add1[18];
        dst[19] = src[19] + add1[19];
        dst[20] = src[20] + add1[20];
        dst[21] = src[21] + add1[21];
        dst[22] = src[22] + add1[22];
        dst[23] = src[23] + add1[23];
        dst[24] = src[24] + add1[24];
        dst[25] = src[25] + add1[25];
        dst[26] = src[26] + add1[26];
        dst[27] = src[27] + add1[27];
        dst[28] = src[28] + add1[28];
        dst[29] = src[29] + add1[29];
        dst[30] = src[30] + add1[30];
        dst[31] = src[31] + add1[31];

        if (StopBefore == 32) return;

        dst[32] = src[32] + add1[32];
        dst[33] = src[33] + add1[33];
        dst[34] = src[34] + add1[34];
        dst[35] = src[35] + add1[35];
        dst[36] = src[36] + add1[36];
        dst[37] = src[37] + add1[37];
        dst[38] = src[38] + add1[38];
        dst[39] = src[39] + add1[39];

        if (StopBefore == 40) return;

        dst[40] = src[40] + add1[40];
        dst[41] = src[41] + add1[41];
        dst[42] = src[42] + add1[42];
        dst[43] = src[43] + add1[43];
        dst[44] = src[44] + add1[44];
        dst[45] = src[45] + add1[45];
        dst[46] = src[46] + add1[46];
        dst[47] = src[47] + add1[47];

        if (StopBefore == 48) return;

        dst[48] = src[48] + add1[48];
        dst[49] = src[49] + add1[49];
        dst[50] = src[50] + add1[50];
        dst[51] = src[51] + add1[51];
        dst[52] = src[52] + add1[52];
        dst[53] = src[53] + add1[53];
        dst[54] = src[54] + add1[54];
        dst[55] = src[55] + add1[55];

        if (StopBefore == 56) return;

        dst[56] = src[56] + add1[56];
        dst[57] = src[57] + add1[57];
        dst[58] = src[58] + add1[58];
        dst[59] = src[59] + add1[59];
        dst[60] = src[60] + add1[60];
        dst[61] = src[61] + add1[61];
        dst[62] = src[62] + add1[62];
        dst[63] = src[63] + add1[63];

        if (StopBefore == 64) return;

        dst[64] = src[64] + add1[64];
        dst[65] = src[65] + add1[65];
        dst[66] = src[66] + add1[66];
        dst[67] = src[67] + add1[67];
        dst[68] = src[68] + add1[68];
        dst[69] = src[69] + add1[69];
        dst[70] = src[70] + add1[70];
        dst[71] = src[71] + add1[71];
        dst[72] = src[72] + add1[72];
        dst[73] = src[73] + add1[73];
        dst[74] = src[74] + add1[74];
        dst[75] = src[75] + add1[75];
        dst[76] = src[76] + add1[76];
        dst[77] = src[77] + add1[77];
        dst[78] = src[78] + add1[78];
        dst[79] = src[79] + add1[79];

        if (StopBefore == 80) return;

        dst[80] = src[80] + add1[80];
        dst[81] = src[81] + add1[81];
        dst[82] = src[82] + add1[82];
        dst[83] = src[83] + add1[83];
        dst[84] = src[84] + add1[84];
        dst[85] = src[85] + add1[85];
        dst[86] = src[86] + add1[86];
        dst[87] = src[87] + add1[87];
        dst[88] = src[88] + add1[88];
        dst[89] = src[89] + add1[89];
        dst[90] = src[90] + add1[90];
        dst[91] = src[91] + add1[91];
        dst[92] = src[92] + add1[92];
        dst[93] = src[93] + add1[93];
        dst[94] = src[94] + add1[94];
        dst[95] = src[95] + add1[95];

        if (StopBefore == 96) return;

        dst[ 96] = src[ 96] + add1[ 96];
        dst[ 97] = src[ 97] + add1[ 97];
        dst[ 98] = src[ 98] + add1[ 98];
        dst[ 99] = src[ 99] + add1[ 99];
        dst[100] = src[100] + add1[100];
        dst[101] = src[101] + add1[101];
        dst[102] = src[102] + add1[102];
        dst[103] = src[103] + add1[103];
        dst[104] = src[104] + add1[104];
        dst[105] = src[105] + add1[105];
        dst[106] = src[106] + add1[106];
        dst[107] = src[107] + add1[107];
        dst[108] = src[108] + add1[108];
        dst[109] = src[109] + add1[109];
        dst[110] = src[110] + add1[110];
        dst[111] = src[111] + add1[111];

        if (StopBefore == 112) return;

        dst[112] = src[112] + add1[112];
        dst[113] = src[113] + add1[113];
        dst[114] = src[114] + add1[114];
        dst[115] = src[115] + add1[115];
        dst[116] = src[116] + add1[116];
        dst[117] = src[117] + add1[117];
        dst[118] = src[118] + add1[118];
        dst[119] = src[119] + add1[119];
        dst[120] = src[120] + add1[120];
        dst[121] = src[121] + add1[121];
        dst[122] = src[122] + add1[122];
        dst[123] = src[123] + add1[123];
        dst[124] = src[124] + add1[124];
        dst[125] = src[125] + add1[125];
        dst[126] = src[126] + add1[126];
        dst[127] = src[127] + add1[127];

    }

}



public static unsafe class PolicyUnrollThings
{
#if AVX512
    private const int N = 32;
#else
    private const int N = 16;
#endif

    private const int HL = PolicyNetwork.L1_SIZE;
    private const int StopBefore = HL / N;

    [MethodImpl(Inline)]
    public static void Add(short* _src, short* _dst, short* _add1)
    {
        VShort* src = (VShort*)_src;
        VShort* dst = (VShort*)_dst;
        VShort* add1 = (VShort*)_add1;

        dst[0] = src[0] + add1[0];

        if (StopBefore == 1) return;

        dst[1] = src[1] + add1[1];

        if (StopBefore == 2) return;

        dst[2] = src[2] + add1[2];
        dst[3] = src[3] + add1[3];

        if (StopBefore == 4) return;

        dst[4] = src[4] + add1[4];
        dst[5] = src[5] + add1[5];
        dst[6] = src[6] + add1[6];
        dst[7] = src[7] + add1[7];

        if (StopBefore == 8) return;

        dst[8] = src[8] + add1[8];
        dst[9] = src[9] + add1[9];
        dst[10] = src[10] + add1[10];
        dst[11] = src[11] + add1[11];
        dst[12] = src[12] + add1[12];
        dst[13] = src[13] + add1[13];
        dst[14] = src[14] + add1[14];
        dst[15] = src[15] + add1[15];

        if (StopBefore == 16) return;

        dst[16] = src[16] + add1[16];
        dst[17] = src[17] + add1[17];
        dst[18] = src[18] + add1[18];
        dst[19] = src[19] + add1[19];
        dst[20] = src[20] + add1[20];
        dst[21] = src[21] + add1[21];
        dst[22] = src[22] + add1[22];
        dst[23] = src[23] + add1[23];
        dst[24] = src[24] + add1[24];
        dst[25] = src[25] + add1[25];
        dst[26] = src[26] + add1[26];
        dst[27] = src[27] + add1[27];
        dst[28] = src[28] + add1[28];
        dst[29] = src[29] + add1[29];
        dst[30] = src[30] + add1[30];
        dst[31] = src[31] + add1[31];

        if (StopBefore == 32) return;

        dst[32] = src[32] + add1[32];
        dst[33] = src[33] + add1[33];
        dst[34] = src[34] + add1[34];
        dst[35] = src[35] + add1[35];
        dst[36] = src[36] + add1[36];
        dst[37] = src[37] + add1[37];
        dst[38] = src[38] + add1[38];
        dst[39] = src[39] + add1[39];

        if (StopBefore == 40) return;

        dst[40] = src[40] + add1[40];
        dst[41] = src[41] + add1[41];
        dst[42] = src[42] + add1[42];
        dst[43] = src[43] + add1[43];
        dst[44] = src[44] + add1[44];
        dst[45] = src[45] + add1[45];
        dst[46] = src[46] + add1[46];
        dst[47] = src[47] + add1[47];

        if (StopBefore == 48) return;

        dst[48] = src[48] + add1[48];
        dst[49] = src[49] + add1[49];
        dst[50] = src[50] + add1[50];
        dst[51] = src[51] + add1[51];
        dst[52] = src[52] + add1[52];
        dst[53] = src[53] + add1[53];
        dst[54] = src[54] + add1[54];
        dst[55] = src[55] + add1[55];

        if (StopBefore == 56) return;

        dst[56] = src[56] + add1[56];
        dst[57] = src[57] + add1[57];
        dst[58] = src[58] + add1[58];
        dst[59] = src[59] + add1[59];
        dst[60] = src[60] + add1[60];
        dst[61] = src[61] + add1[61];
        dst[62] = src[62] + add1[62];
        dst[63] = src[63] + add1[63];

        if (StopBefore == 64) return;

        dst[64] = src[64] + add1[64];
        dst[65] = src[65] + add1[65];
        dst[66] = src[66] + add1[66];
        dst[67] = src[67] + add1[67];
        dst[68] = src[68] + add1[68];
        dst[69] = src[69] + add1[69];
        dst[70] = src[70] + add1[70];
        dst[71] = src[71] + add1[71];
        dst[72] = src[72] + add1[72];
        dst[73] = src[73] + add1[73];
        dst[74] = src[74] + add1[74];
        dst[75] = src[75] + add1[75];
        dst[76] = src[76] + add1[76];
        dst[77] = src[77] + add1[77];
        dst[78] = src[78] + add1[78];
        dst[79] = src[79] + add1[79];

        if (StopBefore == 80) return;

        dst[80] = src[80] + add1[80];
        dst[81] = src[81] + add1[81];
        dst[82] = src[82] + add1[82];
        dst[83] = src[83] + add1[83];
        dst[84] = src[84] + add1[84];
        dst[85] = src[85] + add1[85];
        dst[86] = src[86] + add1[86];
        dst[87] = src[87] + add1[87];
        dst[88] = src[88] + add1[88];
        dst[89] = src[89] + add1[89];
        dst[90] = src[90] + add1[90];
        dst[91] = src[91] + add1[91];
        dst[92] = src[92] + add1[92];
        dst[93] = src[93] + add1[93];
        dst[94] = src[94] + add1[94];
        dst[95] = src[95] + add1[95];

        if (StopBefore == 96) return;

        dst[96] = src[96] + add1[96];
        dst[97] = src[97] + add1[97];
        dst[98] = src[98] + add1[98];
        dst[99] = src[99] + add1[99];
        dst[100] = src[100] + add1[100];
        dst[101] = src[101] + add1[101];
        dst[102] = src[102] + add1[102];
        dst[103] = src[103] + add1[103];
        dst[104] = src[104] + add1[104];
        dst[105] = src[105] + add1[105];
        dst[106] = src[106] + add1[106];
        dst[107] = src[107] + add1[107];
        dst[108] = src[108] + add1[108];
        dst[109] = src[109] + add1[109];
        dst[110] = src[110] + add1[110];
        dst[111] = src[111] + add1[111];

        if (StopBefore == 112) return;

        dst[112] = src[112] + add1[112];
        dst[113] = src[113] + add1[113];
        dst[114] = src[114] + add1[114];
        dst[115] = src[115] + add1[115];
        dst[116] = src[116] + add1[116];
        dst[117] = src[117] + add1[117];
        dst[118] = src[118] + add1[118];
        dst[119] = src[119] + add1[119];
        dst[120] = src[120] + add1[120];
        dst[121] = src[121] + add1[121];
        dst[122] = src[122] + add1[122];
        dst[123] = src[123] + add1[123];
        dst[124] = src[124] + add1[124];
        dst[125] = src[125] + add1[125];
        dst[126] = src[126] + add1[126];
        dst[127] = src[127] + add1[127];

    }

}