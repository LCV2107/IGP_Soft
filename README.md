# IGP_Soft

Programa para mostrar las pantallas de monitoreo del **IGP (Instituto Geofísico del Perú)** a pantalla completa, eligiendo qué se ve en cada monitor.

## 1. Abrir el programa

1. Copia `IGP_Soft.exe` a cualquier carpeta (o a una memoria USB). No necesita instalación.
2. Haz doble clic en `IGP_Soft.exe`.
3. Aparece un panel con **un cuadro por cada monitor**, ubicado como están físicamente. Cada cuadro tiene su número (★ marca el monitor principal) y una vista previa de lo que muestra.

> Si no sabes qué monitor es cuál, pulsa **Identificar monitores**: cada pantalla mostrará su número durante 3 segundos.

## 2. Poner una pantalla en un monitor

1. Haz clic en el cuadro del monitor.
2. Elige un enlace del menú. Se abre al instante a pantalla completa en ese monitor.
3. El cuadro se marca en verde con **✔** y el nombre del enlace.

Para cambiarlo, vuelve a hacer clic en el cuadro y elige otro enlace. Para quitarlo, elige **✖ Cerrar esta ventana**.

Puedes hacer clic y usar normalmente lo que se muestra en cada zona (desplazarte, abrir menús, escribir), pero las ventanas quedan fijas en su lugar: no se pueden mover ni cambiar de tamaño.

Un enlace solo se puede abrir en un lugar a la vez: si ya está abierto, aparece como *(ya abierto)* en los demás monitores.

## 3. Dividir un monitor en varias zonas

Arriba a la derecha de cada cuadro está el control **− n +**:

- **+** divide el monitor en 2, 3, 4, 6, 8 o 9 zonas.
- **−** reduce las zonas (las que sobran se cierran).

Cada zona funciona como una pantalla aparte: haz clic en ella y elige su enlace.

## 4. Mostrar una imagen, un video u otra página

En el menú de cualquier zona, elige **➕ Elemento personalizado (imagen / video / URL)…**:

1. Elige un archivo de imagen o video de tu PC, o pega una dirección web.
2. Si quieres tenerlo siempre disponible, marca la opción para agregarlo a la lista.
3. Acepta: se muestra en esa zona. Los videos se repiten en bucle y sin sonido.

## 5. Adjuntar una ventana de otra aplicación

Puedes poner en una zona cualquier programa que ya tengas abierto (Excel, un visor, otra aplicación de monitoreo, etc.):

1. Abre primero el programa que quieres mostrar.
2. En el panel, haz clic en la zona y elige **▣ Adjuntar ventana abierta**.
3. Elige la ventana de la lista. Se coloca sin bordes y se adapta al tamaño de la zona.

Para quitarla, haz clic en la zona y elige **↩ Soltar ventana**: vuelve al escritorio con su tamaño y posición originales. Al salir de IGP_Soft también se devuelven todas; **nunca se cierran**.

Si guardas un perfil con ventanas adjuntas, al abrirlo se vuelven a adjuntar, siempre que esos programas ya estén abiertos.

> Las aplicaciones de la Tienda de Windows (Configuración, Calculadora…) no se pueden adjuntar. Si un programa se ejecuta como administrador, abre IGP_Soft también como administrador.

## 6. Botones del panel

| Botón | Para qué sirve |
|-------|----------------|
| **Navegador** | Elige con qué navegador se muestran las pantallas (Chrome, Edge, Firefox, Brave u Opera). |
| **Vista previa en vivo** | Muestra u oculta la miniatura de cada monitor dentro del panel. |
| **Omitir la pantalla principal (laptop)** | No usa la pantalla principal (por ejemplo, la laptop desde la que manejas el programa). |
| **Abrir en orden** | Llena todas las zonas de una vez con los enlaces de la lista, en orden. |
| **Perfiles…** | Guarda y abre configuraciones completas de pantallas (ver punto 8). |
| **Enlaces…** | Agrega, edita, ordena o elimina enlaces de la lista. |
| **Identificar monitores** | Muestra el número de cada monitor sobre la pantalla real. |
| **Cerrar todas las ventanas** | Quita todo lo que se está mostrando. |
| **Actualizar** | Vuelve a detectar los monitores (se hace solo al conectar o desconectar uno). |

Para omitir cualquier otro monitor, haz clic en su cuadro y elige **Omitir esta pantalla**.

## 7. Editar la lista de enlaces

Pulsa **Enlaces…**:

- Escribe un **Título** y una **URL** por fila.
- **Archivo…** permite elegir una imagen o un video de tu PC.
- **▲ Subir / ▼ Bajar** cambia el orden (es el orden que usa **Abrir en orden**).
- Pulsa **Guardar**.

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
2. Pulsa **Perfiles… → Guardar pantallas actuales…**
3. Escribe un nombre (por ejemplo, «Sala de monitoreo») y acepta.

**Abrir un perfil**

- Pulsa **Perfiles…**, elige el perfil y pulsa **Abrir ahora** (o haz doble clic en él).
- También puedes hacerlo desde el icono de la bandeja: clic derecho → **Abrir perfil**.

**Última sesión**

El programa guarda solo lo que tienes abierto en el perfil **Última sesión**. Si cierras el programa o se apaga la PC, puedes recuperar todo abriendo ese perfil.

**Abrir algo automáticamente al iniciar**

En **Perfiles…**, en *Al iniciar el programa, abrir*, elige:

- **(nada)**: el panel arranca vacío.
- **Última sesión**: vuelve a abrir lo que tenías la última vez.
- **Un perfil**: abre siempre ese perfil.

Si además marcas **Iniciar IGP_Soft al encender Windows**, las pantallas se abren solas al prender la PC, sin que tengas que hacer nada.

**Acceso directo para cada perfil**

Crea un acceso directo a `IGP_Soft.exe` y, en *Destino*, agrega al final el nombre del perfil:

```
"C:\ruta\IGP_Soft.exe" --perfil="Sala de monitoreo"
```

## 9. Mantener las sesiones iniciadas

Si alguna página pide usuario y contraseña, abre **Perfiles…** y marca **Conservar datos de navegación (cookies, inicios de sesión)**. Así solo tendrás que iniciar sesión una vez y el programa lo recordará las siguientes veces.

Para olvidar esos datos, pulsa **Borrar datos guardados**.

## 10. Ocultar, mostrar y salir

- La **X** del panel no cierra el programa: lo oculta junto al reloj (icono azul **IGP**). Las pantallas siguen mostrándose.
- **Doble clic** en ese icono vuelve a mostrar el panel.
- Para cerrar el programa y todas las pantallas: clic derecho en el icono → **Salir**.
- Si abres `IGP_Soft.exe` cuando ya está abierto, solo aparece el panel que ya estaba abierto.

## Archivos junto al programa

El programa guarda todo en la misma carpeta que `IGP_Soft.exe`. Si copias la carpeta completa a otra PC, se conservan tus enlaces, perfiles y ajustes:

| Archivo | Contiene |
|---------|----------|
| `IGP_Soft.enlaces.txt` | La lista de enlaces |
| `IGP_Soft.perfiles.json` | Los perfiles guardados |
| `IGP_Soft.config.txt` | Navegador elegido y otros ajustes |
| `IGP_Soft_Datos\` | Sesiones iniciadas (solo si activaste el punto 9) |
