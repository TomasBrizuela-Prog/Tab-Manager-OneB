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
    /// lindo, sabe con qué comando y flags lanzarlo, y si acepta una carpeta.
    /// </summary>
    public class ProgramaSoportado
    {
        public string NombreProceso { get; }   // la llave: "Code", "Notepad"...
        public string NombreMostrado { get; }  // "Visual Studio Code"
        public CategoriaPrograma Categoria { get; }
        public string ComandoRelanzar { get; } // ej: "code" (el ejecutable a lanzar)

        // NUEVO: flags fijos que SIEMPRE lleva este programa al lanzarse.
        // Ej: VS Code necesita "--disable-gpu" para renderizar bien tras el reparenting.
        public string ArgumentosBase { get; }

        // NUEVO: ¿este programa acepta abrirse apuntando a una carpeta?
        // Los IDEs sí (code C:\ruta). Apps como Claude no (se lanzan sin ruta).
        public bool UsaCarpeta { get; }

        public ProgramaSoportado(string nombreProceso, string nombreMostrado,
                                 CategoriaPrograma categoria, string comandoRelanzar,
                                 string argumentosBase = "", bool usaCarpeta = false)
        {
            NombreProceso = nombreProceso;
            NombreMostrado = nombreMostrado;
            Categoria = categoria;
            ComandoRelanzar = comandoRelanzar;
            ArgumentosBase = argumentosBase;
            UsaCarpeta = usaCarpeta;
        }
    }

    /// <summary>
    /// El catálogo de todo lo que OneBlack sabe adoptar (la whitelist).
    /// Agregar soporte para un programa nuevo = agregar UNA línea acá.
    /// </summary>
    public static class CatalogoDeProgramas
    {
        private static readonly List<ProgramaSoportado> soportados = new()
        {
            // VS Code: lleva --disable-gpu siempre, y usa carpeta.
            new ProgramaSoportado("Code", "Visual Studio Code", CategoriaPrograma.Ide,
                                  "code", "--disable-gpu", usaCarpeta: true),

            // Ejemplos para el futuro (comentados hasta probarlos):
            // new ProgramaSoportado("idea64",     "IntelliJ IDEA", CategoriaPrograma.Ide, "idea64", "", usaCarpeta: true),
            // new ProgramaSoportado("webstorm64", "WebStorm",      CategoriaPrograma.Ide, "webstorm64", "", usaCarpeta: true),
            // new ProgramaSoportado("claude",     "Claude",        CategoriaPrograma.InteligenciaArtificial, "claude", "", usaCarpeta: false),
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

        /// <summary>
        /// Todos los programas soportados. Útil para la UI del "+" (elegir qué lanzar).
        /// </summary>
        public static IReadOnlyList<ProgramaSoportado> Todos() => soportados;
    }
}