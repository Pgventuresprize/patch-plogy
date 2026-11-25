using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Patch;

/// <summary>
/// Micrófono con cancelación de eco y supresión de ruido, usando el procesador de voz que trae Windows
/// (Voice Capture DSP, el de las llamadas). Le resta al micrófono lo que suena por los parlantes: la voz de PATCH,
/// música, videos. Así no hace falta tener el computador en silencio para hablarle.
/// Entrega audio PCM de 16 kHz, 16 bits, mono (lo mismo que espera Vosk).
/// </summary>
public sealed class CapturaConEco : IDisposable
{
    public event Action<byte[], int>? Datos;
    public event Action<string>? Fallo;

    Thread? hilo;
    volatile bool corriendo;
    readonly ManualResetEventSlim listo = new(false);
    Exception? errorInicio;
    // El procesador necesita que la salida de audio esté activa para saber qué restar (si está quieta, se detiene con
    // 0x87CC000A). Se mantiene sonando silencio: no se oye nada y el procesador queda estable.
    NAudio.Wave.WaveOutEvent? silencio;

    /// <summary>Arranca la captura. Si Windows no puede, lanza la excepción (y quien llama usa el micrófono normal).</summary>
    public void Iniciar()
    {
        try
        {
            silencio = new NAudio.Wave.WaveOutEvent();
            silencio.Init(new NAudio.Wave.SilenceProvider(new NAudio.Wave.WaveFormat(16000, 16, 1)));
            silencio.Play();
        }
        catch (Exception ex) { Registro.Log("No pude mantener activa la salida de audio: " + ex.Message); }
        corriendo = true;
        hilo = new Thread(Bucle) { IsBackground = true, Name = "PATCH-eco" };
        hilo.SetApartmentState(ApartmentState.MTA);
        hilo.Start();
        listo.Wait(TimeSpan.FromSeconds(10));
        if (errorInicio != null) { corriendo = false; throw errorInicio; }
    }

    public void Detener()
    {
        corriendo = false;
        hilo?.Join(2000);
        hilo = null;
        try { silencio?.Stop(); silencio?.Dispose(); } catch { }
        silencio = null;
    }

    public void Dispose() => Detener();

    void Bucle()
    {
        IMediaObject? dmo = null;
        BuferEco? buffer = null;
        IntPtr pBuffer = IntPtr.Zero, formato = IntPtr.Zero;
        try
        {
            var tipo = Type.GetTypeFromCLSID(new Guid("745057C7-F353-4F2D-A7EE-58434477730E"))!;
            var objeto = Activator.CreateInstance(tipo)!;
            dmo = (IMediaObject)objeto;
            var props = (IPropertyStore)objeto;

            // Modo "fuente": el procesador captura solo (micrófono + lo que suena), con eco cancelado en un canal.
            Poner(props, 3, PropVariant.Booleano(true));   // MFPKEY_WMAAECMA_DMO_SOURCE_MODE
            Poner(props, 2, PropVariant.Entero(0));        // MFPKEY_WMAAECMA_SYSTEM_MODE = SINGLE_CHANNEL_AEC
            // Opcional: supresión de ruido y control automático de volumen (sube tu voz si la limpieza la deja baja).
            try
            {
                Poner(props, 5, PropVariant.Booleano(true)); // MFPKEY_WMAAECMA_FEATURE_MODE
                Poner(props, 8, PropVariant.Entero(1));      // MFPKEY_WMAAECMA_FEATR_NS = encendida
                Poner(props, 9, PropVariant.Booleano(Config.Valor("volumen_automatico", "on") != "off")); // MFPKEY_WMAAECMA_FEATR_AGC
            }
            catch (Exception ex) { Registro.Log("Sin control de volumen en la cancelación de eco: " + ex.Message); }

            // Salida: PCM 16 kHz, 16 bits, mono
            formato = Marshal.AllocCoTaskMem(18);
            var wf = new short[] { 1, 1 }; // wFormatTag = PCM, nChannels = 1
            Marshal.Copy(wf, 0, formato, 2);
            Marshal.WriteInt32(formato, 4, 16000);   // nSamplesPerSec
            Marshal.WriteInt32(formato, 8, 32000);   // nAvgBytesPerSec
            Marshal.WriteInt16(formato, 12, 2);      // nBlockAlign
            Marshal.WriteInt16(formato, 14, 16);     // wBitsPerSample
            Marshal.WriteInt16(formato, 16, 0);      // cbSize
            var mt = new DmoMediaType
            {
                majortype = new Guid("73647561-0000-0010-8000-00AA00389B71"),  // MEDIATYPE_Audio
                subtype = new Guid("00000001-0000-0010-8000-00AA00389B71"),    // MEDIASUBTYPE_PCM
                bFixedSizeSamples = 1, lSampleSize = 2,
                formattype = new Guid("05589f81-c356-11ce-bf01-00aa0055595a"), // FORMAT_WaveFormatEx
                cbFormat = 18, pbFormat = formato,
            };
            Comprobar(dmo.SetOutputType(0, ref mt, 0), "SetOutputType");
            Comprobar(dmo.AllocateStreamingResources(), "AllocateStreamingResources");

            buffer = new BuferEco(32000); // 1 s de audio
            pBuffer = buffer.Puntero;
            listo.Set();

            // La estructura de salida va en memoria propia: el traductor automático de .NET no sabe pasar
            // un arreglo de estructuras a esta llamada COM (falla con 0x80131165).
            var salida = Marshal.AllocHGlobal(Marshal.SizeOf<DmoOutputDataBuffer>());
            try
            {
                while (corriendo)
                {
                    Thread.Sleep(10);
                    int estado;
                    do
                    {
                        buffer.Largo = 0;
                        Marshal.StructureToPtr(new DmoOutputDataBuffer { pBuffer = pBuffer }, salida, false);
                        var hr = dmo.ProcessOutput(0, 1, salida, out _);
                        if (hr < 0) throw new COMException($"ProcessOutput devolvió 0x{hr:X8}", hr);
                        estado = Marshal.PtrToStructure<DmoOutputDataBuffer>(salida).dwStatus;
                        if (buffer.Largo > 0)
                        {
                            var datos = new byte[buffer.Largo];
                            Marshal.Copy(buffer.Datos, datos, 0, buffer.Largo);
                            Datos?.Invoke(datos, datos.Length);
                        }
                    } while ((estado & 0x01000000) != 0 && corriendo); // DMO_OUTPUT_DATA_BUFFERF_INCOMPLETE
                }
            }
            finally { Marshal.FreeHGlobal(salida); }
        }
        catch (Exception ex)
        {
            if (!listo.IsSet) { errorInicio = ex; listo.Set(); }
            else Fallo?.Invoke(ex.Message);
        }
        finally
        {
            try { dmo?.FreeStreamingResources(); } catch { }
            if (formato != IntPtr.Zero) Marshal.FreeCoTaskMem(formato);
            buffer?.Dispose();
            if (dmo != null) Marshal.ReleaseComObject(dmo);
        }
    }

    static void Comprobar(int hr, string que) { if (hr < 0) throw new COMException($"El procesador de voz de Windows falló en {que}", hr); }

    static void Poner(IPropertyStore p, int id, PropVariant v)
    {
        var clave = new PropertyKey { fmtid = new Guid("6f52c567-0360-4bd2-9617-ccbf1421c939"), pid = id };
        Comprobar(p.SetValue(ref clave, ref v), $"SetValue({id})");
    }

    // ------------------------------------------------------------------ Interoperabilidad COM

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public int entero;
        [FieldOffset(8)] public short booleano;
        public static PropVariant Entero(int v) => new() { vt = 3, entero = v };             // VT_I4
        public static PropVariant Booleano(bool v) => new() { vt = 11, booleano = (short)(v ? -1 : 0) }; // VT_BOOL
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DmoMediaType
    {
        public Guid majortype, subtype;
        public int bFixedSizeSamples, bTemporalCompression;
        public int lSampleSize;
        public Guid formattype;
        public IntPtr pUnk;
        public int cbFormat;
        public IntPtr pbFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DmoOutputDataBuffer
    {
        public IntPtr pBuffer;
        public int dwStatus;
        public long rtTimestamp, rtTimelength;
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int n);
        [PreserveSig] int GetAt(int i, out PropertyKey k);
        [PreserveSig] int GetValue(ref PropertyKey k, out PropVariant v);
        [PreserveSig] int SetValue(ref PropertyKey k, ref PropVariant v);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("d8ad0f58-5494-4102-97c5-ec798e59bcf4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMediaObject
    {
        [PreserveSig] int GetStreamCount(out int entradas, out int salidas);
        [PreserveSig] int GetInputStreamInfo(int i, out int flags);
        [PreserveSig] int GetOutputStreamInfo(int i, out int flags);
        [PreserveSig] int GetInputType(int i, int tipo, IntPtr mt);
        [PreserveSig] int GetOutputType(int i, int tipo, IntPtr mt);
        [PreserveSig] int SetInputType(int i, IntPtr mt, int flags);
        [PreserveSig] int SetOutputType(int i, ref DmoMediaType mt, int flags);
        [PreserveSig] int GetInputCurrentType(int i, IntPtr mt);
        [PreserveSig] int GetOutputCurrentType(int i, IntPtr mt);
        [PreserveSig] int GetInputSizeInfo(int i, out int tam, out int adelanto, out int alineacion);
        [PreserveSig] int GetOutputSizeInfo(int i, out int tam, out int alineacion);
        [PreserveSig] int GetInputMaxLatency(int i, out long latencia);
        [PreserveSig] int SetInputMaxLatency(int i, long latencia);
        [PreserveSig] int Flush();
        [PreserveSig] int Discontinuity(int i);
        [PreserveSig] int AllocateStreamingResources();
        [PreserveSig] int FreeStreamingResources();
        [PreserveSig] int GetInputStatus(int i, out int flags);
        [PreserveSig] int ProcessInput(int i, IntPtr buffer, int flags, long rtTimestamp, long rtTimelength);
        [PreserveSig] int ProcessOutput(int flags, int cuantos, IntPtr buffers, out int estado);
        [PreserveSig] int Lock(int bloquear);
    }

    /// <summary>
    /// El búfer donde el procesador de Windows deja el audio limpio. Es un objeto COM hecho a mano (tabla de funciones
    /// propia): el puente COM automático de .NET pide una "type library" que no existe y falla con 0x80131165.
    /// </summary>
    public sealed unsafe class BuferEco : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Objeto { public IntPtr vtabla; public IntPtr datos; public int largo; public int maximo; }

        static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
        static readonly Guid IidMediaBuffer = new("59eff8b9-938c-4a26-82f2-95cb84cdc837");
        static IntPtr vtabla;
        readonly Objeto* o;

        /// <summary>El puntero COM (IMediaBuffer*) que se le pasa al procesador.</summary>
        public IntPtr Puntero => (IntPtr)o;
        public IntPtr Datos => o->datos;
        public int Largo { get => o->largo; set => o->largo = value; }

        public BuferEco(int tam)
        {
            if (vtabla == IntPtr.Zero)
            {
                var v = (IntPtr*)Marshal.AllocHGlobal(6 * IntPtr.Size);
                v[0] = (IntPtr)(delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)&QueryInterface;
                v[1] = (IntPtr)(delegate* unmanaged<IntPtr, uint>)&AddRef;
                v[2] = (IntPtr)(delegate* unmanaged<IntPtr, uint>)&Release;
                v[3] = (IntPtr)(delegate* unmanaged<IntPtr, int, int>)&SetLength;
                v[4] = (IntPtr)(delegate* unmanaged<IntPtr, int*, int>)&GetMaxLength;
                v[5] = (IntPtr)(delegate* unmanaged<IntPtr, IntPtr*, int*, int>)&GetBufferAndLength;
                vtabla = (IntPtr)v;
            }
            o = (Objeto*)Marshal.AllocHGlobal(sizeof(Objeto));
            o->vtabla = vtabla;
            o->datos = Marshal.AllocHGlobal(tam);
            o->largo = 0;
            o->maximo = tam;
        }

        [UnmanagedCallersOnly]
        static int QueryInterface(IntPtr self, Guid* iid, IntPtr* ppv)
        {
            if (*iid == IidUnknown || *iid == IidMediaBuffer) { *ppv = self; return 0; }
            *ppv = IntPtr.Zero;
            return unchecked((int)0x80004002); // E_NOINTERFACE
        }
        // La vida del objeto la maneja PATCH (se libera en Dispose), no el conteo de referencias.
        [UnmanagedCallersOnly] static uint AddRef(IntPtr self) => 1;
        [UnmanagedCallersOnly] static uint Release(IntPtr self) => 1;
        [UnmanagedCallersOnly]
        static int SetLength(IntPtr self, int largo)
        {
            var b = (Objeto*)self;
            if (largo < 0 || largo > b->maximo) return unchecked((int)0x80070057); // E_INVALIDARG
            b->largo = largo;
            return 0;
        }
        [UnmanagedCallersOnly] static int GetMaxLength(IntPtr self, int* maximo) { *maximo = ((Objeto*)self)->maximo; return 0; }
        [UnmanagedCallersOnly]
        static int GetBufferAndLength(IntPtr self, IntPtr* datos, int* largo)
        {
            var b = (Objeto*)self;
            if (datos != null) *datos = b->datos;
            if (largo != null) *largo = b->largo;
            return 0;
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(o->datos);
            Marshal.FreeHGlobal((IntPtr)o);
        }
    }
}
