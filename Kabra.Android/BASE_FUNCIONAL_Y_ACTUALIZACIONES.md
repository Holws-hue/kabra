# KABRA 2D — Base Android funcional y procedimiento de actualizaciones

## Base funcional confirmada (27 de noviembre de 2026)

- **Versión:** Kabra Online 2D V48 Fix3, Intersect 0.8.0.339, Android ARM64.
- **Repositorio:** `Holws-hue/kabra`, rama `kabra-2d-android-port-339`.
- **Commit de la corrección:** `7d243286a1a16ce5ccffffe50e0766974337ad83`.
- **Build firmada comprobada en CI:** GitHub Actions run `45936259186`, artifact `15170797246`.
- **Smoke test con render del menú:** run `45937728157`; log contiene `INTERSECT_INITIALIZED` y `GAME_FIRST_RENDER_SUCCESS`.
- **Prueba del propietario:** confirma que instaló esa APK y entró correctamente al juego desde su Motorola.
- **Causa resuelta:** `FileSystemHelper.WriteToTemporaryFolder` accedía a `Assembly.GetEntryAssembly().GetName().Name` (posible null en Android), y el constructor `MonoShader` podía recibir recurso nulo. El parche evita desreferencias y omite shaders XNB DesktopGL en Android por comprobación de plataforma en runtime.
- **Parámetros actuales de Android:** ARM64, `com.kabra.online2d.lanfix3`, UI gráfica inicializada, efectos de iluminación desktop desactivados, objetivo de 30 FPS y MSAA desactivado.
- **No sobrescribir esta rama ni quitar los guards** sin dejar una copia y pruebas de regresión.

## Actualizar el cliente sin perder compatibilidad

Para cambiar menú, imágenes, fondos, tipografías, layouts y recursos, actualizar **solo los archivos modificados bajo `resources/`** de esta rama: `resources/gui/layouts/`, `resources/gui/`, `resources/images/`, `resources/fonts/`, `resources/packs/`, etc. Respetar los nombres y directorios existentes del nuevo cliente y revisar referencias.
El pipeline `.github/workflows/kabra-android.yml` empaqueta desde `resources/` hacia `Kabra.Android/Assets/resources/` en cada build. No es necesario reemplazar `Kabra Online.exe` para cambios de GUI; el EXE de Windows no es ejecutable en Android.
No copiar `config.json` del cliente nuevo a ciegas: conservar/verificar opciones LAN, la clave pública de handshake y los datos de conexión. No subir `gamedata.db` del servidor ni credenciales.

## Para un launcher / actualizador Android de producción

1. Mantener una **versión y firma Android estables** (la CI actual firma con una keystore efímera, así que una APK nueva NO se instala como actualización normal de la anterior).
2. Definir un manifiesto con versión, SHA-256, tamaño y URL de cada recurso; descargar solo archivos modificados al directorio privado de la app; validar hash, usar archivos temporales con reemplazo atómico y conservar caché. No volver a descargar packs de gran tamaño en cada actualización.
3. Distinguir actualización de recursos del juego (puede hacerse en el launcher) de actualización del binario APK (requiere instalación/autorización Android o tienda).
4. Ensayar en emulador y dispositivo real: recursos completos, conexión, login, selección, render de mapa, controles, RAM y FPS; respaldar este baseline funcional para rollback.

**Nota:** el cliente Windows actualizado todavía debe ser comparado con el contenido actual para determinar qué recursos cambiaron. No asumir que un nuevo EXE Windows equivale a una APK Android nueva.
