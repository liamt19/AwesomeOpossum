
using AwesomeOpossum.Logic.Evaluation;
using System.Reflection;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.RuntimeInformation;

namespace AwesomeOpossum.Logic.Util
{
    public static unsafe partial class SIMDBindings
    {
        public static bool HasBindings;

        private static nint Handle;

        private static IntPtr PolicyEvaluateAddr;
        private static IntPtr ValueEvaluateAddr;

        public static unsafe delegate* unmanaged<short*, short*, int> PolicyEvaluateFn;
        public static unsafe delegate* unmanaged<short*, sbyte*, float*, float*, float*, float*, float, float> ValueEvaluateFn;

        private const string MUTEX_NAME = "Global\\AP_SIMD_MUTEX";

#if IsWindows
        private const string DEST_NAME = "SIMDBindings.dll";
#else
        private const string DEST_NAME = "SIMDBindings.so";
#endif

        private static readonly string DLLResouceName;
        private static readonly string AbsoluteDLLDestPath;

        static SIMDBindings()
        {
            DLLResouceName = $"AwesomeOpossum.{DEST_NAME}";
            AbsoluteDLLDestPath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), DEST_NAME);

            TryLoadBindings(verbose: true);
        }


        public static void SetDefaultFuncPtrs()
        {
            HasBindings = false;

            if (Handle != default)
            {
                NativeLibrary.Free(Handle);
                Handle = default;
            }

            PolicyEvaluateFn = (delegate* unmanaged<short*, short*, int>)(&PolicyNetwork.EvaluateImpl);
            ValueEvaluateFn = (delegate* unmanaged<short*, sbyte*, float*, float*, float*, float*, float, float>)(&ValueNetwork.EvaluateImpl);
        }


        public static void TryLoadBindings(bool verbose = false)
        {
            SetDefaultFuncPtrs();

            if (!IsOSPlatform(OSPlatform.Windows) && !IsOSPlatform(OSPlatform.Linux))
                return;

            using (var mutex = new Mutex(false, MUTEX_NAME))
            {
                bool hasHandle = false;
                try
                {
                    hasHandle = mutex.WaitOne(5000);
                    ExtractEmbeddedLibrary(DLLResouceName, DEST_NAME);
                }
                finally
                {
                    if (hasHandle) 
                        mutex.ReleaseMutex();
                }
            }

            if (Handle != default)
            {
                NativeLibrary.Free(Handle);
                Handle = default;
            }

            try
            {
                Handle = NativeLibrary.Load(AbsoluteDLLDestPath);
            }
            catch (Exception e)
            {
                if (verbose)
                    Log($"Failed loading SIMD Bindings! :( \n{e}");

                return;
            }

            var policyFuncName = $"PolicyEvaluate{PolicyNetwork.L1_SIZE}";
            var valueFuncName = $"ValueEvaluate{ValueNetwork.L1_SIZE}_{ValueNetwork.L2_SIZE}_{ValueNetwork.L3_SIZE}";
            try
            {
                PolicyEvaluateAddr = NativeLibrary.GetExport(Handle, policyFuncName);
                ValueEvaluateAddr = NativeLibrary.GetExport(Handle, valueFuncName);

                var SetupNNZAddr = NativeLibrary.GetExport(Handle, "SetupNNZ");
                ((delegate* unmanaged<void>)SetupNNZAddr)();
            }
            catch (Exception e)
            {
                if (verbose)
                    Log($"Failed to find an entry point! \n{e}");

                return;
            }

            PolicyEvaluateFn = (delegate* unmanaged<short*, short*, int>)PolicyEvaluateAddr;
            ValueEvaluateFn = (delegate* unmanaged<short*, sbyte*, float*, float*, float*, float*, float, float>)ValueEvaluateAddr;

            HasBindings = true;

            if (verbose)
                Log("Loaded SIMD Bindings!");
        }


        private static bool ExtractEmbeddedLibrary(string resName, string fileName)
        {
            var asm = Assembly.GetExecutingAssembly();
            //Debug.WriteLine($"looking for {resName} in [{string.Join(", ", asm.GetManifestResourceNames())}]");
            using Stream stream = asm.GetManifestResourceStream(resName);

            if (stream == null)
                return false;

            string exePath = Path.GetDirectoryName(AppContext.BaseDirectory);
            string dllPath = Path.Combine(exePath, fileName);

            try
            {
                using FileStream fs = new(dllPath, FileMode.Create, FileAccess.Write);
                stream.CopyTo(fs);
            } catch (IOException _) { return true; } // This is fine if the file is in use

            return true;
        }
    }
}
