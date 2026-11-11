<div align="center">

<img src="docs/images/icon.png" alt="Linealizador SVG" width="112" />

# Linealizador SVG

**Pasá tus diseños de CorelDRAW a Bambu Studio sin sorpresas: curvas convertidas en segmentos rectos, SVG limpio y huecos que se respetan.**

![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![WPF](https://img.shields.io/badge/UI-WPF-1565C0)
![Tests](https://img.shields.io/badge/tests-32%20passing-2E7D32)
![Bambu Studio](https://img.shields.io/badge/Bambu%20Studio-listo-00AE42)

</div>

---

<p align="center">
  <img src="docs/images/linealizador.png" alt="Convirtiendo un logo para Bambu Studio" width="820" />
</p>

## 🤔 ¿Para qué sirve?

Un SVG exportado de CorelDRAW trae **curvas Bézier, grupos anidados, clases CSS y transformaciones**. Al importarlo en Bambu Studio pasa seguido que aparezcan partes rellenas que tendrían que estar huecas, contornos deformados o piezas que directamente no se importan.

Esta app reconstruye el archivo como Bambu lo entiende mejor: **solo trazados con segmentos rectos, en coordenadas absolutas, cada uno con su color y su regla de relleno**. Las medidas del documento no cambian, así que la pieza sale del tamaño que la diseñaste.

## ✨ Funcionalidades

| | |
|---|---|
| 📐 **Linealiza las curvas** | Bézier cúbicas y cuadráticas (también las suaves `S` y `T`) y arcos elípticos pasan a segmentos rectos |
| 🎚️ **Precisión a elección** | 0,25 (más detalle), 0,5 (recomendada), 0,8 o 1,0 (archivo más liviano). Al cambiarla se vuelve a convertir sola |
| 🧹 **SVG limpio** | Sin grupos, clases CSS, `<defs>` ni estilos heredados: cada trazado lleva su color escrito |
| 🔄 **Aplica las transformaciones** | `translate`, `scale`, `rotate`, `skew` y `matrix` de los grupos quedan aplicadas a las coordenadas |
| 🕳️ **Huecos que funcionan** | El primer trazado va con relleno *nonzero* y los demás con *evenodd*, el perfil validado con el logo de GymUP |
| 📏 **Mismas medidas** | Conserva `width`, `height` y `viewBox`; si falta el `viewBox`, lo arma con el tamaño |
| 👀 **Vista previa** | Ves el resultado con sus rellenos antes de guardar, para detectar un hueco tapado a tiempo |
| 🛟 **Nunca toca el original** | Guarda una copia aparte, `diseño_BAMBU.svg`, en la misma carpeta |
| 🖱️ **Arrastrar y soltar** | Soltá el SVG en la ventana, abrilo con `Ctrl+O` o con *Abrir con…* desde el Explorador |
| 📴 **Sin internet** | Todo pasa en tu PC: los diseños no se suben a ningún lado |

## 🧭 Cómo se usa

1. En CorelDRAW, **convertí los textos a curvas** (`Ctrl+Q`) y exportá como **SVG**.
2. Abrí el SVG en Linealizador SVG (arrastrándolo o con **Abrir SVG**).
3. Mirá la vista previa y, si hace falta, cambiá la precisión.
4. Tocá **Guardar SVG para Bambu** (`Ctrl+S`).
5. Importá el archivo `_BAMBU.svg` en Bambu Studio.

> 💡 Si en la vista previa algo que tendría que estar hueco aparece relleno (o al revés), el diseño tiene otra estructura que la del perfil GymUP: revisalo antes de imprimir.

## 📥 Instalación

Descargá **`LinealizadorSVG.exe`** del [último release](https://github.com/SbaGomez/LinealizadorSVG/releases/latest) y ejecutalo. Es un solo archivo, no necesita instalar nada (ni siquiera .NET).

> 🛡️ La primera vez Windows puede mostrar *"Windows protegió tu PC"*: tocá **Más información → Ejecutar de todas formas**.

## 🛠️ Desarrollo

```powershell
dotnet run --project src/LinealizadorSVG.App   # abrir la app
dotnet test                                    # correr los tests
```

### Generar el `.exe`

```powershell
dotnet publish src/LinealizadorSVG.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

Queda un único `publish/LinealizadorSVG.exe`. El icono se regenera con `powershell -File tools/GenerarIcono.ps1`.

### 🗂️ Estructura

```
src/
├── LinealizadorSVG.Core/     Linealización de trazados, transformaciones y reconstrucción del SVG
└── LinealizadorSVG.App/      Interfaz WPF (MVVM) con vista previa
tests/
└── LinealizadorSVG.Core.Tests/   Tests xUnit de curvas, arcos, estilos, transformaciones y archivos inválidos
tools/
└── GenerarIcono.ps1          Dibuja el icono de la app
```

### 🧰 Hecho con

- [.NET 10](https://dotnet.microsoft.com) + WPF
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet): MVVM
- La conversión es la misma del *Linealizador SVG para Bambu Studio V5* (versión web), portada a C# y comparada contra ella con cientos de trazados

---

<div align="center">

Hecho por **[Sebastian Gomez](https://sebastiangomez.com.ar)**

</div>
