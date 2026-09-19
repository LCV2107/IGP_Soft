# IGP_Soft

Aplicación de escritorio **portable** (un solo `.exe`, sin instalación) para abrir
pantallas web de monitoreo del **IGP (Instituto Geofísico del Perú)** en modo
pantalla completa, eligiendo tú qué enlace va en qué monitor.

## Bandeja e instancia única

- Al pulsar **X** (o `Alt+F4`) el panel **se minimiza a la bandeja del sistema**
  (icono azul «IGP» junto al reloj), no se cierra. Las ventanas de monitoreo
  siguen abiertas.
- **Doble clic** en el icono de la bandeja vuelve a abrir el panel. Clic derecho:
  *Abrir panel · Cerrar todas las ventanas · Salir*.
- **Salir** cierra el programa de verdad (pregunta si quieres cerrar también las
  pantallas abiertas).
- **No se puede abrir dos veces**: si ya está corriendo, un segundo lanzamiento
  solo trae el panel al frente.

## Portable

- `IGP_Soft.exe` no necesita instalación ni .NET: se puede llevar en USB.
- La **lista de enlaces** se guarda en `IGP_Soft.enlaces.txt`, en la misma carpeta
  que el `.exe` (se crea solo la primera vez con los 4 enlaces del IGP).
- No escribe nada en el registro ni en AppData. Los perfiles temporales del
  navegador viven en `%TEMP%` y se borran al reiniciar el programa.

## Cómo funciona

Al abrir `IGP_Soft.exe` aparece un panel que dibuja **cada monitor como un
cuadrado con su número**, colocados según su posición física real. Cada cuadro
muestra una **vista previa en vivo** de lo que hay en ese monitor (se refresca
cada ~1,5 s; se puede apagar con la casilla «Vista previa en vivo»).

- **Dividir un monitor**: cada cuadro tiene arriba un control **`− n +`**. Con
  `+` / `−` partes ese monitor en 1, 2, 3, 4, 6, 8 o 9 **zonas** (en cuadrícula).
  Cada zona se comporta como una pantalla independiente.
- **Clic en una zona** → menú con **todos los enlaces de la lista**. Al elegir
  uno, **se abre de inmediato** en esa zona, sin bordes.
- Reducir el número de zonas cierra las ventanas que sobran; aumentar/repartir
  recoloca las que ya están abiertas.
- Al final del menú: **➕ Elemento personalizado (imagen / video / URL)…** —
  elige un archivo local o pega una URL y se abre al instante en ese monitor.
  Puedes marcarlo para que quede guardado también en la lista.
- **Imágenes y videos** (locales o por URL) se muestran centrados a pantalla
  completa sobre fondo negro; los videos se reproducen en bucle y sin sonido.
- El cuadrado con contenido se marca en verde con **✔ y el nombre**. Un enlace ya
  usado aparece deshabilitado en los demás monitores (no se repite).
- **Clic en un cuadrado que ya tiene ventana** → el menú incluye
  **«✖ Cerrar esta ventana»**. Elegir otro enlace cierra la actual y abre la nueva.

Botones inferiores:

| Control | Acción |
|---------|--------|
| **Omitir la pantalla principal (laptop)** | Marca la pantalla principal como «OMITIDA»: no recibe ningún enlace. Se activa sola cuando hay más monitores que enlaces (p. ej. laptop + 4 TVs) |
| **Abrir en orden** | Reparte los enlaces de la lista, en orden, entre todas las **zonas** de los monitores no omitidos, y las abre de una vez |
| **Enlaces…** | Abre el editor de la lista de enlaces: agregar, editar, reordenar o eliminar. Se guarda en `IGP_Soft.enlaces.txt` |
| **Identificar monitores** | Muestra el número en grande sobre cada pantalla física (como el «Identificar» de Windows), 3 segundos |
| **Cerrar todas las ventanas** | Cierra todas las ventanas abiertas por el programa |
| **Actualizar** | Vuelve a detectar los monitores conectados (también se hace solo al conectar/desconectar una pantalla) |

Cualquier monitor se puede omitir/incluir desde su menú (clic en el cuadrado → «Omitir esta pantalla»).

### Enlaces por defecto

Al primer arranque se crea `IGP_Soft.enlaces.txt` con:

| # | Pantalla | URL |
|---|----------|-----|
| 1 | Último sismo | https://ultimosismo.igp.gob.pe/ultimo-sismo |
| 2 | Reportes sísmicos | https://ultimosismo.igp.gob.pe/productos/reportes-sismicos |
| 3 | Última información volcánica (CENVUL) | https://cenvul.igp.gob.pe/ultima-informacion |
| 4 | Lahares y huaicos | https://grd.igp.gob.pe/lahares-huaicos/ |

Puedes agregar cuantos quieras, desde el botón **Enlaces…** (con **Archivo…** para
elegir imágenes/videos) o editando el archivo a mano (una línea por enlace,
formato `Título | URL`). La "URL" también puede ser una ruta local
(`C:\carpeta\video.mp4`).

## Navegador

En la barra inferior hay un desplegable **Navegador** con todos los compatibles
instalados (**Firefox, Chrome, Edge, Brave, Opera**). Por defecto se marca el
**predeterminado de Windows**; la elección se recuerda en `IGP_Soft.config.txt`.
Si no hay ninguno instalado, el panel muestra un error y no deja abrir pantallas.

Cada pantalla usa un **perfil temporal y aislado** en `%TEMP%\IGP_Soft_Profiles\`
(no requiere cuentas ni credenciales). Los perfiles de sesiones anteriores se
borran al iniciar el programa.

### Modo pantalla

- **Todos los navegadores** abren una ventana **sin bordes ni título** que llena
  exactamente su zona (Chrome/Edge/Brave/Opera en modo `--app`, Firefox con la
  barra oculta). Se les quita el marco por API de Windows para que **no quede
  ningún hueco** por donde se vea el escritorio, ni entre zonas contiguas.
- No se usa `--kiosk`, así que **se cierran** con `Alt+F4` o `Ctrl+W`.
- Cada ventana se reafirma en su sitio cada ~1,5 s (posición, sin bordes y sobre
  la barra de tareas).
- **Modo "pared activa"**: cuando algún monitor (no omitido) queda **totalmente
  cubierto** por ventanas de monitoreo, esas ventanas se mantienen sobre la barra
  de tareas y el panel se pone por encima de todo.
- Mientras **no haya ventanas** abiertas, o solo cubran **parte** de la pantalla,
  todo vuelve a la normalidad: el panel es una ventana común y no estorba.
- Se suprime el aviso de políticas / primer arranque de Firefox (el perfil es
  nuevo cada vez, por eso antes aparecía siempre).
- Cada ventana se localiza por su perfil temporal, así el programa nunca toca
  otras ventanas del navegador que ya tuvieras abiertas.

## Compilar

```bash
dotnet build IGP_Soft.csproj -c Release
```

## Generar el .exe

**Autónomo** (no necesita .NET instalado, ~160 MB — el de `dist\`):

```bash
dotnet publish IGP_Soft.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

**Ligero** (necesita [.NET Desktop Runtime 8+](https://dotnet.microsoft.com/download), ~1 MB):

```bash
dotnet publish IGP_Soft.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true -o dist-light
```

## Notas

- Cerrar una pantalla: `Alt+F4` o `Ctrl+W` dentro de la ventana, o desde el panel
  («Cerrar esta ventana» / «Cerrar todas las ventanas»).
- Los archivos temporales de cada pantalla (perfil + envoltorio de imagen/video)
  se borran al cerrar la ventana y al reiniciar el programa.
- Si cierras una ventana por fuera (`Alt+F4` / `Ctrl+W` / cerrar el proceso), el
  panel lo detecta en ~1,5 s: el monitor vuelve a quedar libre y el enlace se
  puede reutilizar.
- Cada ventana se coloca en su monitor y su posición se reafirma unos segundos.
- Cerrar el panel **no** cierra las pantallas ya abiertas.
- `IGP_Soft.exe --selftest` abre una prueba de consola que verifica la ubicación
  en cada monitor.
- Firefox y Opera no aceptan posición por línea de comandos: su ventana se reubica
  en el monitor destino con la API de Windows justo después de abrirse.
