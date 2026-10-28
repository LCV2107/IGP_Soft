# IGP_Soft

Programa para mostrar las pantallas de monitoreo del **IGP (Instituto Geofísico del Perú)** a pantalla completa, eligiendo qué se ve en cada monitor.

## Descargar e instalar

Descarga la última versión desde [Releases](https://github.com/LCV2107/IGP_Soft/releases/latest). Hay dos formas de usarlo; elige la que prefieras:

| Archivo | Cuándo usarlo |
|---------|---------------|
| **`IGP_Soft-x.y.z.msi`** — instalado | Para el equipo de monitoreo de siempre. Se instala como cualquier programa: queda en el menú Inicio y en el escritorio, y se desinstala desde *Configuración → Aplicaciones*. |
| **`IGP_Soft-x.y.z-portable.exe`** — portable | Para llevarlo en una USB o usarlo sin instalar nada. Cópialo a cualquier carpeta y ábrelo. |

**Instalado:**

1. Abre `IGP_Soft-x.y.z.msi`.
2. Pulsa **Siguiente**, elige la carpeta (o deja la que viene) y pulsa **Instalar**. Windows pedirá permiso de administrador.
3. Al terminar, deja marcada **Abrir IGP_Soft** y pulsa **Finalizar**.

**Portable:**

1. Copia `IGP_Soft-x.y.z-portable.exe` a una carpeta propia (por ejemplo, en una USB). Puedes renombrarlo como quieras, por ejemplo `IGP_Soft.exe`.
2. Haz doble clic en él. No necesita instalación.

> Si Windows muestra «Windows protegió su PC», pulsa **Más información → Ejecutar de todas formas**.

## 1. Abrir el programa

1. Abre **IGP_Soft** desde el menú Inicio o el escritorio (instalado), o con doble clic en el `.exe` (portable).
2. Aparece el panel: a la izquierda, el **menú**; a la derecha, la página **Pantallas** con **un cuadro por cada monitor**, ubicado como están físicamente. Cada cuadro muestra su número (el principal dice «Principal») y una vista previa de lo que muestra.

> Si no sabes qué monitor es cuál, pulsa **Identificar monitores**: cada pantalla mostrará su número durante 3 segundos.

## 2. Poner una pantalla en un monitor

1. Haz clic en el cuadro del monitor.
2. Elige un enlace del menú. Se abre al instante a pantalla completa en ese monitor.
3. La zona muestra una etiqueta con el nombre del enlace: punto **verde** cuando ya está en pantalla, **amarillo** mientras se abre. El borde del monitor se pone verde cuando tiene algo en vivo.

Para cambiarlo, vuelve a hacer clic en el cuadro y elige otro enlace. Para quitarlo, elige **✖ Cerrar esta ventana**.

Puedes hacer clic y usar normalmente lo que se muestra en cada zona (desplazarte, abrir menús, escribir), pero las ventanas quedan fijas en su lugar: no se pueden mover ni cambiar de tamaño.

Un enlace solo se puede abrir en un lugar a la vez: si ya está abierto, aparece como *(ya abierto)* en los demás monitores.

## 3. Dividir un monitor en una grilla

Arriba a la derecha de cada cuadro está el botón **⊞ 1×1 ▾**. Al hacer clic aparece una cuadrícula de 4 × 4:

1. Pasa el mouse sobre los cuadritos: se marcan en azul las **columnas × filas** que vas a usar (por ejemplo, 3 × 2 = 6 zonas).
2. Haz clic para aplicarla.

Puedes elegir cualquier combinación, desde **1 × 1** (pantalla completa) hasta **4 × 4** (16 zonas).

- Lo que ya estaba abierto **se queda en su lugar** si esa zona sigue existiendo en la nueva grilla.
- Las zonas que dejan de existir se cierran.
- Para dividir **todos los monitores igual**, usa **▦ Dividir todos…** arriba de los monitores.

Cada zona funciona como una pantalla aparte: haz clic en ella y elige qué mostrar. La grilla de cada monitor se guarda en los perfiles.

### Unir zonas y elegir cuáles usar

Para armar zonas de distintos tamaños (por ejemplo, una grande y varias chicas alrededor), abre el **diseñador de grilla**:

- Clic en **⊞** → **⊞ Unir zonas o elegir cuáles usar…**, o
- Clic en cualquier zona → **▦ Diseñar grilla de este monitor…**

En el diseñador:

1. Elige **Columnas** y **Filas** (hasta 4 × 4), o una **distribución** lista con un clic: *Grande a la izquierda + 2*, *Grande arriba + 3*, *Grande + 3*, *Grande + 5*, *Grande + 7*.
2. **Arrastra el mouse** sobre las celdas para seleccionarlas (se marcan en amarillo), o haz clic en una zona.
3. Usa los botones:

| Botón | Qué hace |
|-------|----------|
| **⊞ Unir en una zona** | Junta las celdas seleccionadas en una sola zona más grande. |
| **⊟ Separar en celdas** | Vuelve a dividir una zona unida en sus celdas. |
| **⊘ No usar estas zonas** | Deja esas zonas vacías (se ven rayadas). Vuelve a pulsarlo para usarlas otra vez. |
| **↺ Restablecer cuadrícula** | Deshace las uniones y vuelve a la cuadrícula simple. |

4. Pulsa **Aplicar**.

Las zonas en **verde** tienen algo abierto. Al unir zonas, lo que estaba abierto en la zona de **arriba a la izquierda** se queda y se agranda; antes de aplicar, el diseñador te avisa qué se cerraría.

Las zonas **sin usar** no se pueden elegir, y **Abrir en orden** las salta. También puedes desactivar una zona directamente: clic en ella → **⊘ No usar esta zona** (y para volver a usarla: clic → **✓ Usar esta zona**).

## 4. Mostrar una imagen, un video u otra página

En el menú de cualquier zona, elige **➕ Elemento personalizado (imagen / video / URL)…**:

1. Elige un archivo de imagen o video de tu PC, o pega una dirección web.
2. Si quieres tenerlo siempre disponible, deja activado *Agregar también a la lista de enlaces*.
3. Acepta: se muestra en esa zona. Los videos se repiten en bucle y sin sonido.

Las imágenes y videos de tu PC se **copian a la carpeta `Assets`**, junto al programa. Así siguen funcionando aunque muevas o borres el archivo original, o lleves el programa a otra PC.

## 5. Adjuntar una ventana de otra aplicación

Puedes poner en una zona cualquier programa que ya tengas abierto (Excel, un visor, otra aplicación de monitoreo, etc.):

1. Abre primero el programa que quieres mostrar.
2. En el panel, haz clic en la zona y elige **▣ Adjuntar ventana abierta**.
3. Elige la ventana de la lista. Se coloca sin bordes y se adapta al tamaño de la zona.

Para quitarla, haz clic en la zona y elige **↩ Soltar ventana**: vuelve al escritorio con su tamaño y posición originales. Al salir de IGP_Soft también se devuelven todas; **nunca se cierran**.

Si guardas un perfil con ventanas adjuntas, al abrirlo se vuelven a adjuntar, siempre que esos programas ya estén abiertos.

> Las aplicaciones de la Tienda de Windows (Configuración, Calculadora…) no se pueden adjuntar. Si un programa se ejecuta como administrador, abre IGP_Soft también como administrador.

## 6. Menú y páginas

Todo se maneja desde **una sola ventana**. El menú de la izquierda cambia la página, sin abrir ventanas nuevas:

| Página | Para qué sirve |
|--------|----------------|
| **▦ Pantallas** | Los monitores y sus zonas. Arriba tiene los botones rápidos (ver abajo). |
| **☰ Enlaces** | La lista de lo que se puede mostrar (ver punto 7). |
| **▤ Galería** | Tus imágenes y videos (ver punto 12). |
| **★ Perfiles** | Guardar y abrir configuraciones completas de pantallas (ver punto 8). |
| **⇪ Exportar y copias** | .zip, versión portable, copias de seguridad y restaurar (ver punto 11). |
| **⚙ Ajustes** | Navegador, vista previa, pantalla principal, atajos de teclado, rendimiento, qué abrir al iniciar y sesiones guardadas. Todo se aplica al instante. |

Botones rápidos de la página **Pantallas**:

| Botón | Para qué sirve |
|-------|----------------|
| **▶ Abrir en orden** | Llena todas las zonas de una vez con los enlaces de la lista, en orden. |
| **▦ Dividir todos…** | Aplica la misma grilla a todos los monitores (ver punto 3). |
| **⧉ Combinar monitores** | Une varios monitores en una sola pantalla grande (ver abajo). |
| **◎ Identificar monitores** | Muestra el número de cada monitor sobre la pantalla real. |
| **⟳ Actualizar** | Vuelve a detectar los monitores (se hace solo al conectar o desconectar uno). |
| **✖ Cerrar todas** | Quita todo lo que se está mostrando. |

Para no usar la laptop como pantalla de monitoreo, activa en **Ajustes** *No usar la pantalla principal*. Para omitir cualquier otro monitor, haz clic en su cuadro y elige **Omitir esta pantalla**.

### Cambiar el número de los monitores

Los monitores se numeran como los numera Windows. Para cambiarlo, **arrastra un monitor sobre otro** en la página **Pantallas**: el de destino se pinta de azul con *«⇄ Intercambiar con el monitor N»* y, al soltar, intercambian sus números.

El nuevo orden se guarda y se usa en todo: los nombres «Monitor 1 / 2…», **Identificar monitores**, **Abrir en orden**, la galería, los perfiles y los títulos en Alt+Tab. La posición de cada cuadro no cambia (sigue mostrando dónde está físicamente cada pantalla).

Para volver al orden de Windows: clic en una zona → **↺ Restablecer numeración de monitores**.

### Combinar monitores en una sola pantalla

Puedes unir **dos o más monitores** para que funcionen como **una sola pantalla grande**. Por ejemplo, dos de 1920 × 1080 lado a lado forman una de 3840 × 1080: una página puede ocupar ambos, o puedes dividirla en zonas que crucen de un monitor a otro.

1. En **Pantallas**, pulsa **⧉ Combinar monitores**.
2. Haz clic en los monitores que quieres unir (se marcan en azul). Abajo se indica el tamaño de la pantalla resultante.
3. Pulsa **Combinar seleccionados**.

En el panel aparecen como un solo cuadro, **«Monitor N · Combinado (2)»**, y funciona como cualquier otro: lo divides con **⊞**, unes zonas, amplías una zona con los atajos y lo guardas en perfiles. **Identificar monitores** muestra el mismo número en cada pantalla física del grupo.

Para volver a separarlos: clic en una zona → **⊟ Separar monitores combinados** (o **⧉ Combinar monitores → Separar todos**). Los perfiles recuerdan las combinaciones: al abrir un perfil, los monitores se combinan o separan solos según cómo estaban al guardarlo.

> Los monitores combinados deben formar un **rectángulo sin huecos** (por ejemplo 2 lado a lado, 2 uno sobre otro o 4 en 2 × 2) y conviene que tengan la **misma resolución**. Al combinar, se cierra lo que estaba abierto en esos monitores. Si uno se desconecta, los demás vuelven a funcionar por separado hasta que se reconecte.

### Cerrar solo un monitor

Clic en cualquier zona de ese monitor → **✕ Cerrar todo en este monitor**. Se cierran solo sus zonas; los demás monitores siguen igual. (**✖ Cerrar todas**, arriba, cierra todos los monitores.)

## 7. Editar la lista de enlaces

En la página **☰ Enlaces**, edita la tabla directamente. **Los cambios se guardan solos**, no hay que pulsar nada.

- Para agregar un enlace, escribe en la **última fila** (la del asterisco): un **Título** y su **dirección web**.
- **➕ Imagen o video de mi PC…** agrega uno o varios archivos de una vez (se copian a la carpeta `Assets`).
- **▲ Subir / ▼ Bajar** cambia el orden (es el orden que usa **Abrir en orden**).
- **✖ Eliminar fila** quita el enlace seleccionado.

La primera vez la lista trae estos enlaces:

| Pantalla | Dirección |
|----------|-----------|
| Último sismo | https://ultimosismo.igp.gob.pe/ultimo-sismo |
| Reportes sísmicos | https://ultimosismo.igp.gob.pe/productos/reportes-sismicos |
| Última información volcánica (CENVUL) | https://cenvul.igp.gob.pe/ultima-informacion |
| Lahares y huaicos | https://grd.igp.gob.pe/lahares-huaicos/ |

## 8. Perfiles: guardar y abrir configuraciones

Un **perfil** recuerda cómo dejaste todo: qué monitores se usan, en cuántas zonas está dividido cada uno y qué enlace va en cada zona.

**Guardar un perfil**

1. Deja las pantallas como las quieres.
2. En la página **★ Perfiles**, escribe un nombre (por ejemplo, «Sala de monitoreo»).
3. Pulsa **★ Guardar pantallas actuales** (o Enter).

**Abrir un perfil**

- En **★ Perfiles**, haz **doble clic** en el perfil (o elígelo y pulsa **▶ Abrir este perfil**). Te lleva a **Pantallas** con todo abierto.
- También desde el icono de la bandeja: clic derecho → **Abrir perfil**.

**Última sesión**

El programa guarda solo lo que tienes abierto en el perfil **Última sesión**. Si cierras el programa o se apaga la PC, puedes recuperar todo abriendo ese perfil.

**Abrir algo automáticamente al iniciar**

En **⚙ Ajustes**, en *Al iniciar el programa → Abrir*, elige:

- **Nada**: el panel arranca vacío.
- **Última sesión**: vuelve a abrir lo que tenías la última vez.
- **Un perfil**: abre siempre ese perfil.

Si además activas **Iniciar IGP_Soft al encender Windows**, las pantallas se abren solas al prender la PC, sin que tengas que hacer nada.

**Acceso directo para cada perfil**

Crea un acceso directo a `IGP_Soft.exe` y, en *Destino*, agrega al final el nombre del perfil:

```
"C:\ruta\IGP_Soft.exe" --perfil="Sala de monitoreo"
```

## 9. Mantener las sesiones iniciadas

Si alguna página pide usuario y contraseña, en **⚙ Ajustes** activa **Recordar cookies e inicios de sesión de las páginas**. Así solo tendrás que iniciar sesión una vez y el programa lo recordará las siguientes veces.

Para olvidar esos datos, pulsa **Borrar sesiones guardadas**.

## 10. Usar otras ventanas, ocultar y salir

Cada monitor con contenido funciona como una **aplicación a pantalla completa**:

- Puedes abrir o pasar a otro programa con **Alt+Tab**, la barra de tareas o un clic: ese programa aparece **delante** de la pantalla de monitoreo.
- Para volver, usa **Alt+Tab** o su botón en la barra de tareas. Cada pantalla aparece como **«IGP_Soft — Monitor N · lo que muestra»**.
- Mientras usas otro monitor, la pantalla de monitoreo sigue cubriendo todo el suyo (sin la barra de tareas encima).

- La **X** del panel no cierra el programa: lo oculta junto al reloj (icono azul **IGP**). Las pantallas siguen mostrándose.
- **Doble clic** en ese icono vuelve a mostrar el panel.
- Para cerrar el programa y todas las pantallas: clic derecho en el icono → **Salir**.
- Si abres `IGP_Soft.exe` cuando ya está abierto, solo aparece el panel que ya estaba abierto.

## 11. Exportar, versión portable y copias de seguridad

En la página **⇪ Exportar y copias**. Todo incluye tus enlaces, perfiles, ajustes y la carpeta `Assets`, con la misma estructura de carpetas.

| Opción | Qué hace |
|--------|----------|
| **Comprimir en .zip…** | Crea un solo archivo `.zip` para guardar o enviar. |
| **Crear versión portable…** | Crea una carpeta con el programa y todo lo necesario. Cópiala a otra PC (o a una USB) y abre `IGP_Soft.exe`: funciona tal cual. |
| **Copia de seguridad…** | Copia todo en una carpeta nueva con la fecha y hora en el nombre (por ejemplo `IGP_Soft_Copia_2026-10-06_1530`). Nunca sobrescribe nada. |
| **Restaurar…** | Recupera enlaces, perfiles, ajustes y Assets desde un `.zip`, una copia de seguridad o una versión portable. |

Antes de exportar puedes activar, en *Incluir también*:

- **Sesiones guardadas**: incluye las cookies e inicios de sesión del punto 9. Viene desactivado porque son datos sensibles.
- **El programa dentro del .zip**: agrega `IGP_Soft.exe` al `.zip`, para poder usarlo directamente en otra PC.

Si algún enlace apunta a una imagen o video que todavía está fuera de `Assets`, el programa te ofrece copiarlo antes de exportar.

**Al restaurar**, el programa cierra las pantallas abiertas y, antes de reemplazar nada, guarda una copia automática de lo que tenías en la carpeta `Copias de seguridad`. Si te equivocaste, puedes volver atrás restaurando esa copia.

## 12. Galería de recursos

En la página **▤ Galería** ves todo lo que hay en la carpeta `Assets` con su miniatura (imágenes y videos).

- **➕ Agregar archivos…**: elige imágenes o videos de tu PC; se copian a `Assets`. También puedes **arrastrarlos** a la galería.
- **Buscar** por nombre y **filtrar** por Imágenes, Videos u Otros.
- Al elegir un elemento ves su **vista previa** grande, su tamaño, la fecha y si se usa en algún enlace o perfil. Los que están en uso aparecen en **verde**.

Con el elemento elegido puedes:

| Botón | Qué hace |
|-------|----------|
| **Mostrar en una zona ▾** | Elige el monitor y la zona: se abre ahí al instante. |
| **Agregar a la lista de enlaces** | Lo suma a la lista, para usarlo desde cualquier zona o con **Abrir en orden**. |
| **Abrir con el reproductor / visor** | Lo abre con el programa de Windows (útil para ver un video completo). También con doble clic. |
| **Eliminar de Assets** | Lo borra. Si se usa en enlaces o perfiles, te avisa antes. |

## 13. Ampliar una zona a todo el monitor (atajos de teclado)

Cuando un monitor está dividido, puedes **ampliar una zona** para que ocupe **todo el monitor** y verla en detalle. Las demás zonas siguen funcionando detrás. Después la vuelves a su lugar.

**Con el teclado** (funciona desde cualquier programa, aunque el panel esté oculto):

1. Pon el mouse sobre la zona que quieres ver en grande.
2. Pulsa el atajo:

| Atajo | Qué hace |
|-------|----------|
| `Ctrl + Alt + Enter` | Amplía la zona que está bajo el mouse a todo el monitor. |
| `Ctrl + Alt + Retroceso` | Vuelve a la grilla (cada zona a su lugar). |
| `Ctrl + Alt + AvPág` | Amplía la zona siguiente de ese monitor. |
| `Ctrl + Alt + RePág` | Amplía la zona anterior. |
| `Ctrl + Alt + P` | Muestra u oculta el panel (también cuando está oculto en la bandeja). |

**Con el mouse:** clic en la zona (en el panel) → **⤢ Ampliar a todo el monitor**. Para volver: clic → **⤡ Volver a la grilla**.

En el panel, la zona ampliada se marca con la etiqueta amarilla **«Ampliada al monitor»** y las demás aparecen como **«detrás»**.

**Cambiar los atajos:** en **⚙ Ajustes → Atajos de teclado**:

1. Haz clic en el atajo que quieres cambiar (dice *Pulsa la combinación…*).
2. Pulsa la nueva combinación. Debe incluir **Ctrl** o **Alt**.
3. Se guarda al instante. Con **Retroceso** lo dejas *Sin asignar*; con **Esc** cancelas.

También puedes asignar atajos a **Abrir en orden**, **Cerrar todas las ventanas** y **Cerrar todo en el monitor bajo el mouse** (vienen sin asignar). Si la combinación ya la usa otra acción, el programa pregunta si quieres pasársela; si la usa otro programa de Windows, aparece en rojo como **Ocupado**. **↺ Restaurar atajos predeterminados** vuelve a los de la tabla.

> En teclados en español, `Ctrl + Alt` funciona como `AltGr`. Evita combinaciones con letras o números que escriben símbolos (por ejemplo `AltGr + Q` escribe **@**), porque dejarían de escribirse.

## 14. Rendimiento y memoria

IGP_Soft está pensado para muchas pantallas abiertas a la vez sin agotar la memoria del equipo:

- **Un solo navegador para todas las zonas.** Cada zona es una ventana del mismo navegador, en lugar de un navegador completo por zona. Si cierras una zona, las demás siguen funcionando.
- **Navegador «liviano».** Se desactivan servicios que no hacen falta para mostrar pantallas (telemetría, sincronización, actualizaciones de componentes, precarga de páginas) y se limita la cantidad de procesos internos.
- **Imágenes sin navegador.** Las imágenes de tu PC (JPG, PNG, GIF, BMP) se muestran directamente en IGP_Soft, sin abrir un navegador. Los videos y las páginas web siguen usando el navegador.
- **Lo que no se ve, no trabaja.** Cuando amplías una zona (punto 13), las de detrás se ocultan y el navegador deja de dibujarlas hasta que vuelves a la grilla.

En la página **Pantallas**, el chip **Memoria navegador** muestra cuánta RAM usan las pantallas en este momento (también aparece en la tarjeta de estado del menú). Se pone **amarillo** si pasa de la mitad de la RAM del equipo.

En **⚙ Ajustes → Rendimiento**:

| Opción | Para qué sirve |
|--------|----------------|
| **Usar la tarjeta de video** | Viene activada. Desactívala solo si la tarjeta de video es débil o da problemas (se aplica al volver a abrir las pantallas). |
| **Recargar** | *Nunca*, o *cada 6, 12 o 24 horas*. Cierra y vuelve a abrir todas las pantallas en el mismo lugar, para liberar la memoria que las páginas acumulan con los días. Útil en equipos que pasan días encendidos. |
| **⟳ Recargar todo ahora** | Hace esa recarga en el momento. Las pantallas desaparecen unos segundos y vuelven solas. |

## 15. Actualizaciones

IGP_Soft revisa si hay una versión nueva al iniciar y cada 6 horas. **Nunca se actualiza solo:** te avisa y tú decides cuándo.

Cuando hay una versión nueva:

- Aparece un aviso junto al reloj y una barra azul en **Pantallas**: *«Hay una versión nueva de IGP_Soft»*.
- Pulsa **Ver novedades y actualizar** (o haz clic en el aviso). Se abre **⚙ Ajustes → Actualizaciones** con lo que trae la versión nueva.
- Pulsa **⇩ Descargar e instalar**. IGP_Soft descarga la versión, comprueba que el archivo esté intacto, guarda una copia de tus datos, se cierra y **vuelve a abrirse solo con las mismas pantallas**.
- Si lo tienes **instalado**, Windows pedirá permiso de administrador antes de instalar la versión nueva. Si lo tienes **portable**, se actualiza el mismo `.exe`.
- Tus enlaces, perfiles, ajustes, imágenes y sesiones **no se tocan**.

Si no quieres actualizar todavía, cierra la barra con **✕**: no volverá a aparecer para esa versión (seguirá disponible en Ajustes).

En **⚙ Ajustes → Actualizaciones** también puedes:

| Opción | Para qué sirve |
|--------|----------------|
| **Canal** | **Estable**: solo versiones aprobadas (recomendado). **Experimental**: también versiones de prueba, para quien prueba las novedades antes. |
| **Avisarme cuando haya una versión nueva** | Apágalo si no quieres avisos (puedes seguir buscando a mano). |
| **⟳ Buscar actualizaciones** | Revisa en el momento. |
| **↶ Volver a la versión anterior** | Si la versión nueva da problemas, vuelve a la que tenías con un clic (solo en la versión portable). |

> Si IGP_Soft está en una carpeta donde no se puede escribir (por ejemplo, una USB bloqueada), te ofrecerá abrir la página de descarga para actualizar a mano.

## 16. Manejar las pantallas de otro equipo (con LCVDesk)

Desde LCVDesk puedes manejar las pantallas de otro PC con IGP_Soft como si estuvieras sentado frente a él. Los dos equipos deben tener IGP_Soft instalado y **con la misma versión**.

1. En LCVDesk, conéctate al otro equipo y, en **Complementos**, elige **IGP_Soft**.
2. Se abre un panel de IGP_Soft con el nombre del otro equipo arriba a la izquierda (*«Controlando PC-SALA»*). Ahí ves sus monitores, sus enlaces y sus perfiles.
3. Lo que hagas en ese panel ocurre en las pantallas del otro equipo: abrir y cerrar enlaces, dividir en grillas, **Abrir en orden**, **Cerrar todas**, guardar y abrir perfiles. La vista previa de cada monitor muestra lo que tiene en pantalla.
4. Cierra ese panel cuando termines. Las pantallas del otro equipo **siguen abiertas**.

Mientras alguien lo maneja a distancia, el panel del otro equipo muestra **«Controlado desde [equipo]»** y queda bloqueado. Se libera solo al terminar la conexión.

> Por seguridad, a distancia solo se pueden abrir direcciones web (http o https), archivos de la carpeta `Assets` del otro equipo o enlaces que ya estén en su lista.

> Si las versiones no coinciden, IGP_Soft te dirá qué versión tiene cada equipo. Actualiza el que esté atrasado (punto 15).

## Dónde se guardan tus datos

- **Instalado:** en `%LocalAppData%\IGP_Soft` (cada usuario de Windows tiene los suyos). Desinstalar el programa **no** borra tus datos.
- **Portable:** en la misma carpeta que el `.exe`. Si copias la carpeta completa a otra PC, se conservan tus enlaces, perfiles y ajustes.

Para pasar tus datos de una forma a la otra, usa **⇪ Exportar y copias → Copia de seguridad** en una y **Restaurar** en la otra.

| Archivo | Contiene |
|---------|----------|
| `IGP_Soft.enlaces.txt` | La lista de enlaces |
| `IGP_Soft.perfiles.json` | Los perfiles guardados |
| `IGP_Soft.config.txt` | Navegador elegido y otros ajustes |
| `Assets\` | Imágenes y videos que agregaste |
| `IGP_Soft_Datos\` | Sesiones iniciadas (solo si activaste el punto 9) |
| `Copias de seguridad\` | Copias automáticas hechas antes de restaurar o actualizar |
