import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import mkcert from 'vite-plugin-mkcert';

export default defineConfig({
  plugins: [react(), mkcert()],
  server: {
    fs: {
      allow: ['..']
    },
    host: 'localhost',
    port: 3000,
    strictPort: true
  },
  preview: {
    host: '127.0.0.1',
    port: 4173,
    strictPort: true
  },
  build: {
    rollupOptions: {
      // @microsoft/signalr ships /*#__PURE__*/ annotations in positions that
      // Rollup's parser cannot attach. Rollup removes the unparseable
      // annotation and continues; the emitted bundle is correct. The
      // warning is upstream (@microsoft/signalr@10.0.0 is the latest) and
      // strictly cosmetic — silence it so real warnings stay visible.
      onwarn(warning, defaultHandler) {
        if (
          warning.code === 'INVALID_ANNOTATION' &&
          typeof warning.id === 'string' &&
          warning.id.includes('@microsoft/signalr')
        ) {
          return;
        }

        defaultHandler(warning);
      }
    }
  }
});