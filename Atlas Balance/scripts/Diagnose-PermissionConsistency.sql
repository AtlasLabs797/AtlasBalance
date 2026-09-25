-- Read-only diagnostics for historical hierarchical permission rows.
-- Run with a migration/owner connection. This script never changes data.

-- Account scope does not match the account's real country or holder, or the
-- referenced account no longer exists.
SELECT
    p.id AS permiso_id,
    p.usuario_id,
    p.pais_id AS permiso_pais_id,
    p.titular_id AS permiso_titular_id,
    p.cuenta_id,
    c.pais_id AS cuenta_pais_id,
    c.titular_id AS cuenta_titular_id,
    CASE
        WHEN c.id IS NULL THEN 'cuenta_inexistente'
        WHEN p.pais_id IS NOT NULL AND p.pais_id IS DISTINCT FROM c.pais_id THEN 'pais_incompatible'
        WHEN p.titular_id IS NOT NULL AND p.titular_id IS DISTINCT FROM c.titular_id THEN 'titular_incompatible'
        ELSE 'ok'
    END AS diagnostico
FROM "PERMISOS_USUARIO" p
LEFT JOIN "CUENTAS" c ON c.id = p.cuenta_id
WHERE p.cuenta_id IS NOT NULL
  AND (
      c.id IS NULL
      OR (p.pais_id IS NOT NULL AND p.pais_id IS DISTINCT FROM c.pais_id)
      OR (p.titular_id IS NOT NULL AND p.titular_id IS DISTINCT FROM c.titular_id)
  )
ORDER BY p.usuario_id, p.id;

-- Exact duplicate permission rows. They are redundant but not unsafe because
-- authorization is evaluated with EXISTS; remove only after manual review.
SELECT
    usuario_id,
    pais_id,
    titular_id,
    cuenta_id,
    puede_ver_cuentas,
    puede_agregar_lineas,
    puede_editar_lineas,
    puede_eliminar_lineas,
    puede_importar,
    puede_ver_dashboard,
    puede_revisar_lineas,
    puede_aprobar_importaciones,
    puede_conciliar,
    puede_cerrar_conciliacion,
    COUNT(*) AS filas
FROM "PERMISOS_USUARIO"
GROUP BY
    usuario_id,
    pais_id,
    titular_id,
    cuenta_id,
    puede_ver_cuentas,
    puede_agregar_lineas,
    puede_editar_lineas,
    puede_eliminar_lineas,
    puede_importar,
    puede_ver_dashboard,
    puede_revisar_lineas,
    puede_aprobar_importaciones,
    puede_conciliar,
    puede_cerrar_conciliacion
HAVING COUNT(*) > 1
ORDER BY usuario_id, filas DESC;
