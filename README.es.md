# Minecraft Backup Manager

Una herramienta para administrar copias de seguridad y restauraciones de servidores Minecraft Java Edition en Windows.

Minecraft Backup Manager es una aplicación de consola para Windows diseñada para simplificar y automatizar los procesos de copia de seguridad y restauración de servidores Minecraft Java Edition.

La aplicación se comunica con los servidores Minecraft mediante **RCON** para coordinar operaciones seguras antes de realizar respaldos. **Restic** es el motor de copias de seguridad previsto; su integración se encuentra actualmente en desarrollo.

## Tabla de contenidos

- [Características](#características)
- [Estado del proyecto](#estado-del-proyecto)
- [Arquitectura](#arquitectura)
- [Tecnologías](#tecnologías)
- [Requisitos](#requisitos)
- [Licencia](#licencia)

## Características

Las funcionalidades se organizan según su estado actual de desarrollo.

### Implementado

**Infraestructura principal**
- Configuración persistente de la aplicación mediante JSON.
- Validación de la configuración y control de versiones del esquema.
- Gestión centralizada de las rutas de la aplicación.
- Sistema de registros organizado por categorías y niveles de severidad.
- Almacenamiento seguro de credenciales mediante Windows Data Protection API (DPAPI).

**Integración con servidores Minecraft**
- Conexión y autenticación mediante RCON.
- Ejecución asíncrona de comandos RCON.
- Serialización y deserialización de paquetes RCON.
- Preparación del servidor para las operaciones de respaldo: desactivación temporal del guardado automático, escritura de los datos pendientes del mundo y posterior reactivación del guardado automático.

### En desarrollo

**Integración del motor de copias de seguridad**
- Integración con Restic.
- Creación de respaldos y administración de snapshots.
- Copias de seguridad deduplicadas y versionadas.
- Categorías de respaldos y políticas de retención.

### Planificado

**Administración de restauraciones**
- Restauración de respaldos completos del servidor.
- Selección de snapshots específicos.
- Creación de respaldos previos a una restauración.
- Validación de las operaciones de restauración para ayudar a reducir el riesgo de pérdida de datos.

**Automatización**
- Programación de copias de seguridad automáticas mediante el Programador de tareas de Windows.
- Ejecución de operaciones programadas sin intervención manual.

## Estado del proyecto

🚧 **En desarrollo**

La infraestructura principal y la integración RCON con servidores Minecraft ya están implementadas. La integración con Restic y los flujos de trabajo de copias de seguridad y restauración todavía están en desarrollo. Las funcionalidades y la documentación pueden cambiar a medida que avance el proyecto.

## Arquitectura

La aplicación está organizada en componentes con responsabilidades diferenciadas:

| Componente | Responsabilidad |
| --- | --- |
| `Core` | Inicio y coordinación de la aplicación. |
| `Configuration` | Carga, almacenamiento y validación de la configuración. |
| `Infrastructure` | Rutas centralizadas y servicios de infraestructura. |
| `Logging` | Registro de eventos de la aplicación por categorías. |
| `Minecraft` | Coordinación del servidor Minecraft y comunicación RCON. |
| `Security` | Almacenamiento seguro de credenciales sensibles. |
| `Restic` | Integración prevista del motor de copias de seguridad; actualmente en desarrollo. |
| `UI` | Menús de la aplicación mediante consola. |

Las pruebas automatizadas se encuentran en el proyecto `MinecraftBackupManager.Tests`.

## Tecnologías

- **Lenguaje:** C#
- **Framework:** .NET 10
- **IDE:** Visual Studio 2022
- **Comunicación con el servidor:** Minecraft RCON
- **Protección de credenciales:** Windows Data Protection API (DPAPI)
- **Pruebas:** MSTest
- **Motor de copias de seguridad:** Restic (integración en desarrollo)
- **Automatización:** Programador de tareas de Windows (planificado)

## Requisitos

### Requisitos actuales

- Sistema operativo Windows.
- .NET 10 Runtime.
- Un servidor Minecraft Java Edition.
- RCON habilitado y configurado en el servidor Minecraft.

### Requisito adicional para la integración de copias de seguridad

- Ejecutable de Restic (será necesario cuando la integración esté disponible).

## Licencia

Este proyecto está bajo la [licencia MIT](LICENSE).

Copyright © 2026 Mateo Noriega.
