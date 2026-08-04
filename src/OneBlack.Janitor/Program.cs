using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace OneBlack.Janitor
{
    internal class Program
    {
        // --- P/Invoke: esperar al proceso ---
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        // --- P/Invoke: restaurar ventanas (mismo arsenal que el adoptador) ---
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const uint SYNCHRONIZE = 0x00100000;
        private const uint INFINITE = 0xFFFFFFFF;
        private const int GWL_STYLE = -16;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private static readonly string CarpetaDatos = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OneBlack");
        private static readonly string RutaLog = Path.Combine(CarpetaDatos, "janitor.log");
        private static readonly string RutaEstado = Path.Combine(CarpetaDatos, "ventanas-adoptadas.json");

        // Copia local del modelo persistido (debe coincidir con el de OneBlack.Core).
        private class VentanaPersistida
        {
            public long Hwnd { get; set; }
            public long PadreOriginal { get; set; }
            public int EstilosOriginales { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Ancho { get; set; }
            public int Alto { get; set; }
        }

        static void Main(string[] args)
        {
            if (args.Length < 1 || !uint.TryParse(args[0], out uint pidOneBlack))
            {
                Log("Janitor arrancó sin un PID válido. Saliendo.");
                return;
            }

            Log($"Janitor vigilando a OneBlack (PID {pidOneBlack}).");

            IntPtr handle = OpenProcess(SYNCHRONIZE, false, pidOneBlack);
            if (handle == IntPtr.Zero)
            {
                Log($"No pude abrir el proceso {pidOneBlack}. Saliendo.");
                return;
            }

            WaitForSingleObject(handle, INFINITE);
            CloseHandle(handle);

            Log("OneBlack terminó. El janitor se despertó.");

            // ¿Quedó trabajo sin limpiar? La presencia de ventanas en el archivo
            // ES la señal de crash. Si no hay archivo, fue cierre limpio.
            RevisarYRestaurar();
        }

        private static void RevisarYRestaurar()
        {
            if (!File.Exists(RutaEstado))
            {
                Log("No hay archivo de estado: cierre limpio. Nada que restaurar.");
                return;
            }

            List<VentanaPersistida> ventanas;
            try
            {
                string json = File.ReadAllText(RutaEstado);
                ventanas = JsonSerializer.Deserialize<List<VentanaPersistida>>(json)
                           ?? new List<VentanaPersistida>();
            }
            catch (Exception ex)
            {
                Log($"No pude leer el archivo de estado: {ex.Message}");
                return;
            }

            if (ventanas.Count == 0)
            {
                Log("Archivo de estado vacío. Nada que restaurar.");
                return;
            }

            Log($"¡CRASH detectado! {ventanas.Count} ventana(s) huérfana(s). Restaurando...");

            foreach (var v in ventanas)
            {
                try
                {
                    IntPtr hwnd = new IntPtr(v.Hwnd);
                    IntPtr padre = new IntPtr(v.PadreOriginal);

                    // Misma secuencia que Devolver: estilos, padre, posición.
                    SetWindowLong(hwnd, GWL_STYLE, v.EstilosOriginales);
                    SetParent(hwnd, padre);
                    SetWindowPos(hwnd, IntPtr.Zero, v.X, v.Y, v.Ancho, v.Alto,
                        SWP_NOZORDER | SWP_FRAMECHANGED);

                    Log($"  Restaurada ventana HWND {v.Hwnd}.");
                }
                catch (Exception ex)
                {
                    Log($"  Falló restaurar HWND {v.Hwnd}: {ex.Message}");
                }
            }

            // Limpiar el archivo: el trabajo de recuperación terminó.
            try { File.Delete(RutaEstado); } catch { }
            Log("Restauración completa. Archivo de estado limpiado.");
        }

        private static void Log(string mensaje)
        {
            Directory.CreateDirectory(CarpetaDatos);
            File.AppendAllText(RutaLog, $"{DateTime.Now:HH:mm:ss} — {mensaje}{Environment.NewLine}");
        }
    }
}