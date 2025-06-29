
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Drawing;
using System.Linq;
using System.Text;

namespace AwesomeOpossum.Generators;

[Generator]
public sealed class ValueGenerator : IIncrementalGenerator
{
    private static readonly int[] Sizes = [64, 128, 256, 512, 768, 1024, 1280, 1536, 1792, 2048];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            var funcs = string.Concat(Sizes.Select(x => EvalImplStub.Replace("<L1_SIZE>", $"{x}")));
            string src = EvalImplHeaderFooter.Replace("<VALUE_IMPL_FUNCS>", funcs);

            ctx.AddSource("ValueNetworkImpls.g.cs", SourceText.From(src, Encoding.UTF8));
        });
    }



    public const string EvalImplHeaderFooter =
"""
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
namespace AwesomeOpossum.Logic.Evaluation;
public static unsafe partial class ValueNetwork {
<VALUE_IMPL_FUNCS>
}
""";



    public const string EvalImplStub =
"""

[UnmanagedCallersOnly]
public static int EvaluateImpl<L1_SIZE>(short* stmData, short* ntmData, short* l1Weights, short l1Bias)
{
    const int L1_SIZE = <L1_SIZE>;

    Vector256<short> maxVec = Vector256.Create((short)QA);
    Vector256<short> zeroVec = Vector256<short>.Zero;
    Vector256<int> sum = Vector256<int>.Zero;

    int SimdChunks = L1_SIZE / Vector256<short>.Count;

    var ourData = (Vector256<short>*)stmData;
    var theirData = (Vector256<short>*)ntmData;
    var ourWeights = (Vector256<short>*)(&l1Weights[0]);
    var theirWeights = (Vector256<short>*)(&l1Weights[L1_SIZE]);
    for (int i = 0; i < SimdChunks; i++)
    {
        Vector256<short> clamp = Vector256.Min(maxVec, Vector256.Max(zeroVec, ourData[i]));
        Vector256<short> mult = clamp * ourWeights[i];

        (var mLo, var mHi) = Vector256.Widen(mult);
        (var cLo, var cHi) = Vector256.Widen(clamp);

        sum = Vector256.Add(sum, Vector256.Add(mLo * cLo, mHi * cHi));
    }

    for (int i = 0; i < SimdChunks; i++)
    {
        Vector256<short> clamp = Vector256.Min(maxVec, Vector256.Max(zeroVec, theirData[i]));
        Vector256<short> mult = clamp * theirWeights[i];

        (var mLo, var mHi) = Vector256.Widen(mult);
        (var cLo, var cHi) = Vector256.Widen(clamp);

        sum = Vector256.Add(sum, Vector256.Add(mLo * cLo, mHi * cHi));
    }

    int output = Vector256.Sum(sum);
    output = (output / QA) + l1Bias;

    return output * OUTPUT_SCALE / (QA * QB);
}

""";
}