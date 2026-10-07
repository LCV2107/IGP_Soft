# IGP_Soft

Programa para mostrar las pantallas de monitoreo del **IGP (Instituto Geofísico del Perú)** a pantalla completa, eligiendo qué se ve en cada monitor.

## 1. Abrir el programa

1. Copia `IGP_Soft.exe` a cualquier carpeta (o a una memoria USB). No necesita instalación.
2. Haz doble clic en `IGP_Soft.exe`.
3. Aparece el panel: a la izquierda, el **menú**; a la derecha, la página **Pantallas** con **un cuadro por cada monitor**, ubicado como están físicamente. Cada cuadro muestra su número (el principal dice «Principal») y una vista previa de lo que muestra.

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
| **⚙ Ajustes** | Navegador, vista previa, pantalla principal, qué abrir al iniciar y sesiones guardadas. Todo se aplica al instante. |

Botones rápidos de la página **Pantallas**:

| Botón | Para qué sirve |
|-------|----------------|
| **▶ Abrir en orden** | Llena todas las zonas de una vez con los enlaces de la lista, en orden. |
| **▦ Dividir todos…** | Aplica la misma grilla a todos los monitores (ver punto 3). |
| **◎ Identificar monitores** | Muestra el número de cada monitor sobre la pantalla real. |
| **⟳ Actualizar** | Vuelve a detectar los monitores (se hace solo al conectar o desconectar uno). |
| **✖ Cerrar todas** | Quita todo lo que se está mostrando. |

Para no usar la laptop como pantalla de monitoreo, activa en **Ajustes** *No usar la pantalla principal*. Para omitir cualquier otro monitor, haz clic en su cuadro y elige **Omitir esta pantalla**.

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

## Archivos junto al programa

El programa guarda todo en la misma carpeta que `IGP_Soft.exe`. Si copias la carpeta completa a otra PC, se conservan tus enlaces, perfiles y ajustes:

| Archivo | Contiene |
|---------|----------|
| `IGP_Soft.enlaces.txt` | La lista de enlaces |
| `IGP_Soft.perfiles.json` | Los perfiles guardados |
| `IGP_Soft.config.txt` | Navegador elegido y otros ajustes |
| `Assets\` | Imágenes y videos que agregaste |
| `IGP_Soft_Datos\` | Sesiones iniciadas (solo si activaste el punto 9) |
| `Copias de seguridad\` | Copias automáticas hechas antes de restaurar |
