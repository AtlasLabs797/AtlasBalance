# Cómo contribuir a AtlasBalance

¡Gracias por tu interés en contribuir a **AtlasBalance**! Todas las aportaciones son bienvenidas, desde reportar errores hasta proponer nuevas funciones o escribir código.

Para mantener el proyecto organizado y seguro, te pedimos que sigas estas directrices:

## 💡 Sugerir Funciones o Reportar Errores

Si tienes una idea o encontraste un fallo, por favor usa la pestaña de **Issues**:

1. Ve a la pestaña [Issues](../../issues) y haz clic en **New Issue**.
2. Selecciona la plantilla adecuada (**Bug report** para errores o **Feature request** para sugerencias).
3. Rellena el formulario con la mayor cantidad de detalles posible. 
   *(Nota: Para problemas graves de seguridad, lee nuestro archivo `SECURITY.md`).*

## 💻 Contribuir con Código

La rama `main` está **protegida**. No se aceptan subidas de código (pushes) directas. Todo el código nuevo debe integrarse mediante un **Pull Request (PR)** y ser aprobado por un administrador.

### Flujo de trabajo (Workflow):

1. **Haz un Fork** de este repositorio hacia tu cuenta de GitHub.
2. **Clona** tu fork localmente:
   `git clone https://github.com/TU-USUARIO/AtlasBalance.git`
3. **Crea una rama nueva** para tu función o corrección. Usa un nombre descriptivo:
   `git checkout -b feature/nueva-funcion` o `git checkout -b fix/error-login`
4. **Haz tus cambios** y asegúrate de que el código funcione correctamente.
5. **Sube los cambios (Commit & Push)** a tu repositorio:
   `git add .`
   `git commit -m "Añade una descripción clara de lo que hiciste"`
   `git push origin nombre-de-tu-rama`
6. **Abre un Pull Request** desde tu fork hacia la rama `main` de este repositorio.

### Revisión de Código
Una vez abierto el PR, el administrador del repositorio revisará tu código. Es posible que te dejemos comentarios o te pidamos algunos ajustes antes de aprobar y hacer el merge definitivo (Squash merge).

¡Gracias por hacer de AtlasBalance un proyecto mejor!
