using System;
using System.Collections.Generic;
using System.Linq;

namespace OneBlack.Core
{
    /// <summary>Categoría de un programa adoptable, para agrupar en la UI.</summary>
    public enum CategoriaPrograma
    {
        Ide,
        Editor,
        InteligenciaArtificial
    }

    /// <summary>
    /// Un programa que OneBlack sabe adoptar. Cada uno es, en la práctica,
    /// un "adaptador": lo identifica por su nombre de proceso, sabe mostrarlo
    /// lindo, y (a futuro) sabrá cómo relanzarlo para la persistencia de sesión.
    /// </summary>
    public class ProgramaSoportado
    {
        public string NombreProceso { get; }   // la llave: "Code", "Notepad"...
        public string NombreMostrado { get; }  // "Visual Studio Code"
        public CategoriaPrograma Categoria { get; }
        public string ComandoRelanzar { get; } // ej: "code" (para reabrir sesión)

        public ProgramaSoportado(string nombreProceso, string nombreMostrado,
                                 CategoriaPrograma categoria, string comandoRelanzar)
        {
            NombreProceso = nombreProceso;
            NombreMostrado = nombreMostrado;
            Categoria = categoria;
            ComandoRelanzar = comandoRelanzar;
        }
    }

    /// <summary>
    /// El catálogo de todo lo que OneBlack sabe adoptar (la whitelist).
    /// Agregar soporte para un programa nuevo = agregar UNA línea acá.
    /// </summary>
    public static class CatalogoDeProgramas
    {
        // La whitelist. Arrancamos con lo que podemos probar hoy; el resto
        // se suma con una línea cada uno cuando se quiera soportar y probar.
        private static readonly List<ProgramaSoportado> soportados = new()
        {
            new ProgramaSoportado("Code",    "Visual Studio Code", CategoriaPrograma.Ide,    "code"),
            new ProgramaSoportado("Notepad", "Bloc de notas",      CategoriaPrograma.Editor, "notepad"),

            // Ejemplos para el futuro (comentados hasta probarlos):
            // new ProgramaSoportado("idea64",    "IntelliJ IDEA", CategoriaPrograma.Ide, "idea64"),
            // new ProgramaSoportado("webstorm64","WebStorm",      CategoriaPrograma.Ide, "webstorm64"),
            // new ProgramaSoportado("claude",    "Claude",        CategoriaPrograma.InteligenciaArtificial, "claude"),
        };

        /// <summary>
        /// ¿Este nombre de proceso está en la whitelist? Devuelve el programa
        /// soportado, o null si no lo soportamos.
        /// </summary>
        public static ProgramaSoportado? Buscar(string nombreProceso)
        {
            return soportados.FirstOrDefault(p =>
                p.NombreProceso.Equals(nombreProceso, StringComparison.OrdinalIgnoreCase));
        }
    }
}