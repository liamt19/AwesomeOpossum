
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;

namespace AwesomeOpossum.Logic.Util;

public static class TensorUtil
{

    [MethodImpl(Inline)]
    public static void SoftmaxTensor(Span<float> tensor, float temperature)
    {
        var maxScore = TensorPrimitives.Max(tensor);

        TensorPrimitives.Subtract(tensor, maxScore, tensor);
        TensorPrimitives.Divide(tensor, temperature, tensor);
        TensorPrimitives.Exp(tensor, tensor);
    }


    [MethodImpl(Inline)]
    public static void NormalizeTensor(Span<float> tensor)
    {
        var total = TensorPrimitives.Sum(tensor);

        TensorPrimitives.Divide(tensor, total, tensor);
    }

}
