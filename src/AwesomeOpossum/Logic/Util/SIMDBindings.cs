
using AwesomeOpossum.Logic.Evaluation;
using System.Reflection;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.RuntimeInformation;

namespace AwesomeOpossum.Logic.Util
{
    public static unsafe partial class SIMDBindings
    {
        public static readonly bool HasBindings;

        private static readonly nint Handle;

        private static readonly IntPtr PolicyEvaluateAddr;
        private static readonly IntPtr ValueEvaluateAddr;

        public static unsafe delegate* unmanaged<short*, short*, short*, int> PolicyEvaluateFn;
        public static unsafe delegate* unmanaged<short*, short*, short*, short, int> ValueEvaluateFn;

#if IsWindows
        private const string DEST_NAME = "SIMDBindings.dll";
#else
        private const string DEST_NAME = "SIMDBindings.so";
#endif

        static SIMDBindings()
        {
            HasBindings = false;

            var valueFuncName = $"EvaluateImpl{ValueNetwork.L1_SIZE}";

            try
            {
                PolicyEvaluateFn = (delegate* unmanaged<short*, short*, short*, int>)(&PolicyNetwork.EvaluateImpl);
                ValueEvaluateFn = (delegate* unmanaged<short*, short*, short*, short, int>)
                    typeof(ValueNetwork).GetMethod(valueFuncName, BindingFlags.Public | BindingFlags.Static).MethodHandle.GetFunctionPointer();
            }
            catch (NullReferenceException e)
            {
                var m = typeof(ValueNetwork).GetMethods(BindingFlags.Public | BindingFlags.Static).ToList();
                Console.WriteLine("It appears that the source generator didn't run!");
                Console.WriteLine($"There should be a method called {valueFuncName} in this list -> [{string.Join(", ", m.Select(x => x.Name))}].");
                Console.WriteLine($"This is going to report a bad bench and exit.");

                Console.WriteLine($"info string 1 seconds");
                Console.WriteLine($"1 nodes 1 nps");
                Environment.Exit(0);
            }

            if (!IsOSPlatform(OSPlatform.Windows) && !IsOSPlatform(OSPlatform.Linux))
                return;

            string resName = $"AwesomeOpossum.{DEST_NAME}";
            string absPath = Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory), DEST_NAME);

            try
            {
                if (!ExtractEmbeddedLibrary(resName, DEST_NAME) && !File.Exists(absPath))
                {
                    return;
                }

                Handle = NativeLibrary.Load(absPath);
            }
            catch (Exception e)
            {
                Log($"Failed loading SIMD Bindings! :( \n{e}");
                return;
            }

            try
            {
                PolicyEvaluateAddr = NativeLibrary.GetExport(Handle, "PolicyEvaluate");
                ValueEvaluateAddr = NativeLibrary.GetExport(Handle, $"ValueEvaluate{ValueNetwork.L1_SIZE}");
            }
            catch (Exception e)
            {
                Log($"Failed to find an entry point! \n{e}");
                return;
            }

            PolicyEvaluateFn = (delegate* unmanaged<short*, short*, short*, int>)PolicyEvaluateAddr;
            ValueEvaluateFn = (delegate* unmanaged<short*, short*, short*, short, int>)ValueEvaluateAddr;

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

            try
            {
                using FileStream fs = new(dllPath, FileMode.Create, FileAccess.Write);
                stream.CopyTo(fs);
            } catch (IOException _) { return true; } // This is fine if the file is in use

            return true;
        }

    }
}
