# Minecraft Backup Manager

Una herramienta de administración de copias de seguridad y restauraciones para servidores de Minecraft Java Edition en Windows.

Minecraft Backup Manager es una aplicación de consola para Windows diseñada para simplificar y automatizar los flujos de copia de seguridad y restauración de servidores de Minecraft Java Edition.

La aplicación se comunica con los servidores de Minecraft mediante **RCON** para coordinar operaciones seguras del servidor antes de cada copia de seguridad, y utiliza **Restic** como motor de respaldo.

## Tabla de contenido

- [Características](#características)
- [Cómo funcionan los respaldos](#cómo-funcionan-los-respaldos)
- [Tipos de respaldo y retención](#tipos-de-respaldo-y-retención)
- [Estado del proyecto](#estado-del-proyecto)
- [Arquitectura](#arquitectura)
- [Tecnologías](#tecnologías)
- [Requisitos](#requisitos)
- [Licencia](#licencia)

## Características

Las características se agrupan según su estado de desarrollo actual.

### Implementado

El motor de respaldo está implementado y cubierto por pruebas automatizadas. Todavía no se puede usar desde los menús de consola (consulta [En desarrollo](#en-desarrollo)).

**Infraestructura principal**
- Configuración persistente de la aplicación en JSON, con validación y versionado del esquema.
- Gestión centralizada de las rutas de la aplicación.
- Registro (logs) por categorías con varios niveles de severidad.
- Almacenamiento seguro de credenciales mediante la API de Protección de Datos de Windows (DPAPI).

**Integración con servidores de Minecraft**
- Cliente RCON implementado desde cero: conexión, autenticación, ejecución asíncrona de comandos, tiempos de espera y serialización y deserialización de paquetes.
- Preparación del servidor para los respaldos: desactivación temporal del guardado automático y escritura de los datos pendientes del mundo. El guardado automático siempre se reactiva después, incluso si el respaldo falla o se cancela.
- Detección de servidor apagado: si la conexión RCON es rechazada, la aplicación comprueba el archivo `session.lock` del mundo. Si el mundo está libre, el respaldo se hace sin RCON; si está bloqueado, se informa un error claro.

**Motor de respaldo (Restic)**
- Integración con Restic: inicialización y comprobación del estado del repositorio, respaldo, listado de snapshots, limpieza de bloqueos obsoletos, retención (`forget`) y compactación (`prune`).
- Snapshots cifrados, deduplicados y versionados, etiquetados según el tipo de respaldo.
- Ejecutable de Restic incluido con la aplicación o personalizado.
- Los archivos bloqueados no cancelan un respaldo: el snapshot se crea y los archivos que no se pudieron leer se informan como advertencias.

**Coordinación de respaldos**
- Un solo respaldo a la vez, con informes estructurados para cada resultado (completado, completado con advertencias, fallido, ya en curso u omitido).
- Política de retención según el tipo de respaldo (consulta [Tipos de respaldo y retención](#tipos-de-respaldo-y-retención)).
- Opción para omitir los respaldos automáticos mientras el servidor está apagado.

**Programación automática**
- Planificador por horas integrado, por defecto en el minuto :00.
- El tipo de cada respaldo automático (weekly, daily u hourly) se elige a partir de los snapshots existentes.
- Los respaldos automáticos se pueden pausar y reanudar. La pausa se guarda solo en memoria, así que al reiniciar la aplicación vuelven a estar activos.
- Los ciclos que se pierdan mientras el equipo estaba apagado o suspendido se omiten; no se recuperan.

### En desarrollo

**Interfaz de consola**
- Menú de configuración (rutas, contraseñas y opciones) y flujo de primer uso.
- Menú de copias de seguridad: respaldo inicial, respaldo manual y pausa o reanudación de los respaldos automáticos.
- Conexión del planificador con la aplicación.

Por ahora solo funciona el menú de información; los menús de configuración, copias de seguridad y restauraciones son marcadores de posición.

### Planificado

**Gestión de restauraciones**
- Restaurar copias de seguridad completas del servidor.
- Seleccionar snapshots específicos para restaurar.
- Crear copias de seguridad previas a la restauración.
- Validar las restauraciones y comprobar la integridad del repositorio para ayudar a reducir el riesgo de pérdida de datos.

**Ejecución en segundo plano**
- Mantener la aplicación en el área de notificaciones para que los respaldos automáticos continúen al cerrar la ventana.
- Iniciar automáticamente con Windows.

**Mantenimiento**
- Gestión del tamaño de los logs.

## Cómo funcionan los respaldos

1. Se comprueba el repositorio de Restic antes de tocar el servidor.
2. La aplicación se conecta al servidor mediante RCON.
3. Se desactiva el guardado automático (`save-off`) y se escriben en disco los datos pendientes del mundo (`save-all flush`).
4. Restic crea un snapshot de la carpeta del servidor.
5. Se reactiva el guardado automático (`save-on`). Esto ocurre siempre, incluso si hay un error o se cancela el respaldo.
6. Se aplica la política de retención a los respaldos automáticos.
7. Se informa el resultado y se escribe en el registro de respaldos.

Si el servidor está apagado, se omiten los pasos 2, 3 y 5 y el respaldo se hace directamente, después de confirmar que el mundo no está en uso.

## Tipos de respaldo y retención

Cada snapshot se etiqueta con su tipo:

| Tipo | Cuándo se crea | Retención |
| --- | --- | --- |
| `initial` | Primer respaldo tras la configuración | Nunca se elimina automáticamente |
| `manual` | A petición del usuario | Nunca se elimina automáticamente |
| `hourly` | Automáticamente, cuando no toca weekly ni daily | Los últimos 6 por defecto (de 1 a 24) |
| `daily` | Primer respaldo automático de cada día | Los últimos 7 por defecto (hasta 7) |
| `weekly` | Primer respaldo automático de cada semana (de lunes a domingo) | Los últimos 4 por defecto (hasta 4) |

Los snapshots antiguos se eliminan tras cada respaldo automático, y el repositorio se compacta como máximo una vez al día. Los valores de retención se guardan en el archivo de configuración.

## Estado del proyecto

🚧 **En desarrollo**

La infraestructura principal, la integración con RCON de Minecraft, la integración con Restic y el motor de respaldo y programación están implementados y probados. Los menús de consola que los exponen y el flujo de restauración siguen en desarrollo. Las características y la documentación pueden cambiar a medida que avanza el proyecto.

## Arquitectura

La aplicación está organizada en componentes con responsabilidades distintas:

| Componente | Responsabilidad |
| --- | --- |
| `Core` | Inicio y coordinación de la aplicación. |
| `UI` | Menús de la aplicación en consola. |
| `Configuration` | Carga, guardado y validación de la configuración. |
| `Infrastructure` | Rutas centralizadas de la aplicación y servicios de infraestructura. |
| `Logging` | Registro de la aplicación por categorías. |
| `Security` | Almacenamiento seguro de credenciales sensibles. |
| `Minecraft` | Comunicación RCON, preparación del servidor para los respaldos y detección de servidor apagado. |
| `Restic` | Ejecución e interpretación de las operaciones de Restic. |
| `Backup` | Coordinación de respaldos, retención, elección del tipo de respaldo y programación. |

Las pruebas automatizadas se mantienen en el proyecto `MinecraftBackupManager.Tests`.

## Tecnologías

- **Lenguaje:** C#
- **Framework:** .NET 10
- **IDE:** Visual Studio 2022
- **Comunicación con el servidor:** RCON de Minecraft
- **Protección de credenciales:** API de Protección de Datos de Windows (DPAPI)
- **Motor de respaldo:** Restic
- **Pruebas:** MSTest

## Requisitos

- Sistema operativo Windows.
- Runtime de .NET 10.
- Un servidor de Minecraft Java Edition con RCON habilitado y configurado.
- El ejecutable de Restic, desarrollado y probado con la versión 0.19.1. La aplicación busca `Tools\restic.exe` junto a su ejecutable, o usa una ruta personalizada definida en la configuración. El ejecutable no se incluye en este repositorio.
- Una ubicación para el repositorio de respaldos fuera de la carpeta del servidor, preferiblemente en otro disco.

> **Importante:** la contraseña del repositorio de Restic se guarda solo en el archivo de credenciales cifrado de la aplicación. Sin ella no se pueden abrir las copias de seguridad, así que guarda una copia en un lugar seguro.

## Licencia

Este proyecto está bajo la [Licencia MIT](LICENSE).

Copyright © 2026 Mateo Noriega.