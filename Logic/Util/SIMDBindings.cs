
using System.Reflection;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.RuntimeInformation;

namespace AwesomeOpossum.Logic.Util
{
    public static unsafe partial class SIMDBindings
    {
        public static readonly bool HasBindings;
        private static readonly nint Handle;

#if IsWindows
        private const string DEST_NAME = "SIMDBindings.dll";
#else
        private const string DEST_NAME = "SIMDBindings.so";
#endif

        static SIMDBindings()
        {
            HasBindings = false;
            if (!IsOSPlatform(OSPlatform.Windows) && !IsOSPlatform(OSPlatform.Linux))
                return;

            string resName = $"AwesomeOpossum.{DEST_NAME}";
            string absPath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), DEST_NAME);

            try
            {
                if (!ExtractEmbeddedLibrary(resName, DEST_NAME))
                {
                    return;
                }

                Handle = NativeLibrary.Load(absPath);
            }
            catch (Exception e)
            {
                Log("Failed loading SIMD Bindings! :(");
                Log(e.Message);
                return;
            }

            HasBindings = true;
            Log("Loaded SIMD Bindings!");
        }

        private static bool ExtractEmbeddedLibrary(string resName, string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            Debug.WriteLine($"looking for {resName} in [{string.Join(", ", asm.GetManifestResourceNames())}]");
            using Stream stream = asm.GetManifestResourceStream(resName);

            if (stream == null)
            {
                //Log("Running without SIMD Bindings");
                return false;
            }

            string exePath = Path.GetDirectoryName(AppContext.BaseDirectory);
            string dllPath = Path.Combine(exePath, fileName);

            using FileStream fs = new(dllPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(fs);

            return true;
        }


        [LibraryImport(DEST_NAME, EntryPoint = "PolicyEvaluate")]
        public static partial int EvaluatePolicy(short* us, short* them, short* L1Weights);

        [LibraryImport(DEST_NAME, EntryPoint = "ValueEvaluate")]
        public static partial int EvaluateValue(short* us, short* them, short* L1Weights, short L1Biases);
    }
}
