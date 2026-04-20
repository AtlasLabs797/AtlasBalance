1. Versión comercial

Atlas Balance es una plataforma privada de tesorería para controlar en un solo sitio los saldos, movimientos y previsiones de varias cuentas, titulares y divisas. Facilita la importación de extractos, el seguimiento rápido de operaciones, el control de permisos por usuario y la trazabilidad completa de todo cambio. Además, automatiza alertas de saldo bajo, backups, exportaciones y sincronización de tipos de cambio, reduciendo trabajo manual y errores.

2. Versión técnica

Atlas Balance es una aplicación web on-premise pensada para funcionar dentro de la red local de la empresa, con backend en ASP.NET Core 8, frontend en React 18 + TypeScript y base de datos PostgreSQL. Está orientada a la gestión de tesorería multi-banco, multi-titular y multi-divisa, con importación flexible de extractos, tabla tipo Excel para consulta y edición de movimientos, dashboards financieros, auditoría detallada, permisos granulares y automatizaciones como backups, exportaciones y actualización del sistema. También integra OpenClaw mediante token seguro para consultas financieras controladas.

3. Versión por módulos

Gestión financiera: centraliza cuentas bancarias, titulares y cajas de efectivo.
Multi-divisa: trabaja con varias monedas, muestra totales convertidos y sincroniza tipos de cambio.
Importación de extractos: permite pegar datos desde Excel o CSV y mapear columnas por banco.
Tabla operativa: muestra movimientos en una vista tipo Excel con edición, filtros, ordenación y columnas extra.
Dashboards: ofrece visión global y por titular con saldos, ingresos, egresos y evolución histórica.
Permisos: controla quién ve, edita o exporta cada parte de la información.
Auditoría: registra cambios con usuario, fecha, IP, valor anterior y valor nuevo.
Alertas: avisa por email y dentro de la app cuando una cuenta baja de cierto saldo.
Automatización: ejecuta backups, exportaciones mensuales, limpieza de tokens y sincronización de divisas.
Mantenimiento del sistema: soporta actualización desde la interfaz y restauración desde backups.
Integración externa: permite acceso controlado para OpenClaw con tokens limitados y auditados.
