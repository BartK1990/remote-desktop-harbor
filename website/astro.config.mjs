// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Published to GitHub Pages by .github/workflows/docs.yml. See ADR-0013.
export default defineConfig({
	site: 'https://bartk1990.github.io',
	base: '/remote-desktop-harbor',
	integrations: [
		starlight({
			title: 'Remote Desktop Harbor',
			description:
				'A fast, portable Windows app that keeps Remote Desktop connections and credentials in one encrypted catalog.',
			logo: { src: './src/assets/logo.svg' },
			favicon: '/favicon.svg',
			customCss: ['./src/styles/custom.css'],
			social: [
				{ icon: 'github', label: 'GitHub', href: 'https://github.com/BartK1990/remote-desktop-harbor' },
			],
			editLink: {
				baseUrl: 'https://github.com/BartK1990/remote-desktop-harbor/edit/master/website/',
			},
			lastUpdated: true,
			sidebar: [
				{ label: 'Introduction', link: '/' },
				{
					label: 'Getting started',
					items: [
						'getting-started/installation',
						'getting-started/quick-start',
						'getting-started/portable-mode',
					],
				},
				{
					label: 'Using the app',
					items: [
						'guides/managing-entries',
						'guides/importing-rdp-files',
						'guides/connecting',
						'guides/keyboard-shortcuts',
					],
				},
				{
					label: 'Team setup',
					items: [
						'team/shared-database',
						'team/vault-key-protection',
						'team/entra-id',
						'team/session-presence',
						'team/offline-mode',
					],
				},
				{
					label: 'Reference',
					items: [
						'reference/settings',
						'reference/data-and-files',
						'reference/security-model',
						'reference/data-model',
						'reference/troubleshooting',
					],
				},
				{
					label: 'Development',
					items: [
						'development/building',
						'development/architecture',
						'development/spec-driven-workflow',
						'development/database-providers',
						'development/ci-and-releases',
					],
				},
			],
		}),
	],
});
