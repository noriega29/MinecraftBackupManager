# Minecraft Backup Manager

**Una herramienta para gestionar copias de seguridad y restauraciones de servidores Minecraft Java Edition en Windows.**

Minecraft Backup Manager es una aplicación de consola para Windows diseñada para simplificar y automatizar la creación y restauración de copias de seguridad de servidores Minecraft Java Edition.

La aplicación utiliza [Restic](https://restic.net/) como motor de copias de seguridad y se comunica con los servidores Minecraft mediante RCON para coordinar operaciones de respaldo seguras.

## Características

### Gestión de copias de seguridad
- Crear y gestionar copias de seguridad de servidores Minecraft.
- Coordinar el guardado del servidor mediante RCON antes de realizar una copia de seguridad.
- Utilizar Restic para crear copias de seguridad deduplicadas y versionadas.
- Admitir diferentes categorías de copias de seguridad y políticas de retención.

### Gestión de restauraciones
- Restaurar copias de seguridad completas del servidor.
- Seleccionar instantáneas específicas para su restauración.
- Ofrecer restauraciones seguras mediante copias de seguridad previas a la operación.
- Validar las operaciones de restauración para reducir el riesgo de pérdida de datos.

### Seguridad
- Proteger las credenciales sensibles mediante Windows DPAPI.
- Almacenar la configuración de la aplicación en un archivo persistente.

### Automatización
- Programar copias de seguridad automáticas mediante el Programador de tareas de Windows.
- Ejecutar operaciones programadas sin necesidad de intervención manual.

## Tecnologías utilizadas

- **Lenguaje:** C#
- **IDE:** Visual Studio 2022
- **Framework:** .NET
- **Motor de copias de seguridad:** Restic
- **Comunicación con el servidor:** RCON
- **Protección de credenciales:** Windows DPAPI
- **Automatización:** Programador de tareas de Windows

## Estado del proyecto

<escape>🚧</escape> **En desarrollo**

Minecraft Backup Manager se encuentra actualmente en desarrollo. Sus funcionalidades y documentación evolucionarán a medida que avance el proyecto.

## Requisitos

La aplicación está siendo desarrollada para entornos Windows y requiere:

- Sistema operativo Windows.
- Runtime de .NET compatible con la aplicación.
- Ejecutable de Restic.
- Un servidor Minecraft Java Edition con RCON habilitado.

## Licencia

Este proyecto está distribuido bajo la [Licencia MIT](LICENSE).

Copyright © 2026 Mateo Noriega.