// Inter with its optical-size axis: large figures and titles get the tighter "Display" cut automatically.
import '@fontsource-variable/inter/opsz.css';
import '@fontsource-variable/inter-tight/wght.css';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
// Global styles first so component styles (imported through App) can override them.
import './styles/tokens.css';
import './styles/base.css';
import { App } from './App';

const root = document.getElementById('root');
if (!root) throw new Error('Missing #root element');

createRoot(root).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
