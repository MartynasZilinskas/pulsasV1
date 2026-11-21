import { Link } from '@tanstack/react-router'
import { Button } from '@ui-sdk/react/button'

export function SectionPage({ section }: { section: string }) {
  return (
    <section className="app-section" aria-labelledby="section-heading">
      <h1 id="section-heading">{section}</h1>
      <div className="app-section-message">
        <p>This section is not built yet.</p>
        <p>The {section} view has no live data connection. You can explore the available interface components in the Kitchen Sink.</p>
        <Button className="app-section-link" render={<Link to="/kitchen-sink" />}>Explore the Kitchen Sink</Button>
      </div>
    </section>
  )
}
