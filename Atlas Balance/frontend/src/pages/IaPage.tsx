import { AiChatPanel } from '@/components/ia/AiChatPanel';

export default function IaPage() {
  return (
    <section className="ia-page">
      <header className="dashboard-toolbar">
        <div>
          <h1>IA</h1>
          <p className="dashboard-subtitle">Consultas financieras usando los datos reales visibles para tu usuario.</p>
        </div>
      </header>

      <AiChatPanel />
    </section>
  );
}
