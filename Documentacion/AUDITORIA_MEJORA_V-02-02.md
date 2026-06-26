# Auditoría de Mejora y Roadmap Técnico - V-02-02

Fecha: 2026-06-09
Versión: `V-02-02`
Producto: Atlas Balance

## 1. Resumen Ejecutivo
Tras realizar una revisión técnica y arquitectónica de Atlas Balance, se confirma que la aplicación cuenta con una base sólida y madura, especialmente en seguridad y gestión de estados. Sin embargo, el crecimiento del producto ha generado cierta "obesidad" en controladores y servicios clave, y existen oportunidades de optimización en el cálculo de métricas financieras para grandes volúmenes de datos.

## 2. Hallazgos por Área

### A. Arquitectura Backend (ASP.NET Core 8)
- **Controladores Obesos**: `IntegrationOpenClawController` (>1200 líneas) y `UsuariosController` (>1000 líneas) acumulan demasiada lógica. Se recomienda extraer subtareas a servicios especializados.
- **Servicios Monolíticos**: `AtlasAiService` (>2700 líneas) gestiona desde prompts hasta auditoría y cuotas. Es un candidato crítico para refactorización.
- **Lógica en Controladores**: Algunos controladores aún realizan validaciones complejas o proyecciones de datos que deberían residir en la capa de servicios.

### B. Rendimiento y Escalabilidad
- **Cálculo de Métricas**: `DashboardService.BuildMetricsAsync` y similares realizan conversiones de divisa iterando sobre los resultados. En instalaciones con decenas de miles de movimientos, esto penaliza el tiempo de respuesta. Se recomienda:
    - Implementar agregados cacheados para totales históricos.
    - Realizar conversiones en bloque (bulk) o directamente en SQL si es posible.
- **Base de Datos**: Los índices actuales cubren las búsquedas básicas. Sin embargo, el aumento de filtros por `pais_id` y `titular_id` en `V-02-02` sugiere la necesidad de revisar índices compuestos para evitar scans completos en tablas de movimientos.

### C. Frontend (React 18)
- **Virtualización**: Excelente uso de `@tanstack/react-virtual` para tablas grandes.
- **Deuda de Estilos**: Gran dependencia de `system-coherence.css` para corregir inconsistencias. Indica que los componentes base (cards, inputs) necesitan una unificación en sus propias definiciones CSS.
- **Competencia de Overlays**: En mobile, la coexistencia de Bottom Nav, Bottom Sheet y Chat IA flotante genera una ergonomía mejorable.

### D. Seguridad y Resiliencia
- **Exposición de Errores**: Se usan manejadores globales, pero algunos controladores devuelven `ex.Message` de excepciones específicas. Aunque no exponen stack traces, pueden revelar detalles de la estructura de datos interna.
- **Resiliencia**: El mecanismo de actualización y el Watchdog son robustos, pero el "bootstrap" desde versiones muy antiguas sigue siendo un punto de fricción operativa.

## 3. Roadmap de Mejora (Prioridades)

### P1 - Urgente / Alto Impacto
1.  **Refactor de Servicios Críticos**: Partir `AtlasAiService` en sub-servicios (Config, Chat, Quota, Audit).
2.  **Optimización de Dashboard**: Cambiar el cálculo iterativo de saldos convertidos por una estrategia de agregación más eficiente.
3.  **Harness de Verificación Visual**: Implementar las capturas automatizadas con Playwright (propuestas en la auditoría UI/UX) para garantizar que no haya regresiones visuales en rutas P0.

### P2 - Necesario / Deuda Técnica
1.  **Limpieza de Controladores**: Mover lógica de `IntegrationOpenClawController` a servicios de integración dedicados.
2.  **Unificación de Contratos**: Asegurar que `CuentasController.Resumen` y el resumen de extractos compartan el mismo DTO y lógica.
3.  **Refactor CSS**: Mover reglas de `system-coherence.css` a componentes base y eliminar estilos inline residuales.

### P3 - Deseable / Polish
1.  **Mejora de Tooltips**: Unificar la experiencia de tooltips en gráficas (actualmente `TitularSaldoBarChart` usa el default de Recharts).
2.  **Jerarquía de Admin**: Reforzar visualmente la diferencia entre acciones normales y acciones de riesgo sistémico (ej. restaurar backup).

## 4. Conclusión
Atlas Balance es un producto técnicamente superior a la media de apps internas, con controles de seguridad envidiables. El foco de los próximos ciclos de desarrollo debería ser la **limpieza arquitectónica** para evitar que el código se vuelva inmanejable y la **optimización de procesos de agregación** para asegurar que el dashboard siga siendo instantáneo con datos reales masivos.
