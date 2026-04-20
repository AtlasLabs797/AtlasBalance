import { Link, useNavigate } from 'react-router-dom';

export default function NotFoundPage() {
  const navigate = useNavigate();

  return (
    <section className="page-placeholder">
      <h1>404</h1>
      <p>La ruta que pediste no existe o ya fue movida.</p>
      <div className="not-found-actions">
        <button type="button" onClick={() => navigate(-1)}>
          Volver atrás
        </button>
        <Link to="/dashboard">Ir al dashboard</Link>
      </div>
    </section>
  );
}
