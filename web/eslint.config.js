import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  {
    ignores: [
      'dist',
      'eslint.config.js',
      'playwright-report',
      'coverage',
      'src/shared/api/generated/contracts.generated.ts'
    ]
  },
  js.configs.recommended,
  ...tseslint.configs.recommendedTypeChecked,
  {
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2023,
      globals: {
        ...globals.browser,
        ...globals.node
      },
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname
      }
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      'no-restricted-syntax': [
        'error',
        {
          selector: "CallExpression[callee.name='fetch']",
          message: 'Use RTK Query through the shared API layer instead of calling fetch directly.'
        },
        {
          selector: "MemberExpression[object.name='window'][property.name='fetch']",
          message: 'Use RTK Query through the shared API layer instead of calling fetch directly.'
        },
        {
          selector: "MemberExpression[object.name='localStorage']",
          message: 'Do not store browser auth state in localStorage.'
        },
        {
          selector: "MemberExpression[object.name='sessionStorage']",
          message: 'Do not store browser auth state in sessionStorage.'
        }
      ]
    }
  },
  {
    files: ['src/features/**/*.{ts,tsx}'],
    rules: {
      'no-restricted-syntax': [
        'error',
        {
          selector: "NewExpression[callee.name='WebSocket']",
          message: 'Use the shared realtime adapter instead of creating feature-local WebSocket connections.'
        },
        {
          selector: "NewExpression[callee.name='EventSource']",
          message: 'Use the shared realtime adapter instead of creating feature-local EventSource connections.'
        },
        {
          selector: "ImportDeclaration[source.value='@microsoft/signalr']",
          message: 'Use the shared realtime adapter instead of importing SignalR directly in feature code.'
        }
      ]
    }
  }
);