type StartupScreenProps = {
  actionLabel?: string;
  detail: string;
  eyebrow?: string;
  onAction?: () => void;
  title: string;
};

export function StartupScreen({ actionLabel, detail, eyebrow = 'Baseline shell', onAction, title }: StartupScreenProps) {
  return (
    <section className="startup-screen" aria-live="polite">
      <p className="startup-screen__eyebrow">{eyebrow}</p>
      <h1 className="startup-screen__title">{title}</h1>
      <p className="startup-screen__copy">The frontend shell stays thin and lets server capabilities drive the experience.</p>
      <p className="startup-screen__detail">{detail}</p>

      {actionLabel && onAction ? (
        <div className="startup-screen__actions">
          <button className="button" type="button" onClick={onAction}>
            {actionLabel}
          </button>
        </div>
      ) : null}
    </section>
  );
}