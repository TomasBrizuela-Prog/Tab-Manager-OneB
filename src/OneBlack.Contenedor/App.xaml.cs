using System;
using System.Windows;
using System.Windows.Threading;

namespace OneBlack.Contenedor
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Manotazo de ahogado #1: excepciones en el hilo de UI (lo más común en WPF).
            DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Manotazo de ahogado #2: excepciones en cualquier otro hilo.
            AppDomain.CurrentDomain.UnhandledException += App_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            SoltarVentanasDeEmergencia();
            // No marcamos e.Handled: dejamos que la app muera igual, pero ya soltó las ventanas.
        }

        private void App_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            SoltarVentanasDeEmergencia();
        }

        private void SoltarVentanasDeEmergencia()
        {
            try
            {
                if (MainWindow is MainWindow ventana)
                    ventana.SoltarTodoDeEmergencia();
            }
            catch
            {
                // En un manotazo de ahogado, si esto falla, no hay más que hacer.
                // El janitor es la última red.
            }
        }
    }
}