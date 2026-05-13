# Como contribuir a AtlasBalance

AtlasBalance es una app de tesoreria. Eso exige cambios pequenos, revisables y sin sorpresas.

## Bugs y propuestas

Usa `Issues` para bugs y mejoras funcionales. Incluye:

- version afectada;
- pasos para reproducir;
- resultado esperado;
- resultado real;
- capturas o logs saneados si ayudan.

No publiques vulnerabilidades en issues. Para seguridad, usa `SECURITY.md`.

## Pull requests

`main` no acepta pushes directos. Todo cambio entra por pull request.

Flujo recomendado:

1. Haz fork del repositorio.
2. Clona tu fork:

```powershell
git clone https://github.com/TU-USUARIO/AtlasBalance.git
```

3. Crea una rama descriptiva:

```powershell
git checkout -b fix/login-healthcheck
```

4. Haz cambios acotados y prueba lo que toques.
5. Sube la rama:

```powershell
git add .
git commit -m "Corrige healthcheck de login"
git push origin fix/login-healthcheck
```

6. Abre un pull request hacia `main`.

No mezcles refactors, cambios visuales y fixes de seguridad en el mismo PR. Parece productivo hasta que toca revisarlo.
