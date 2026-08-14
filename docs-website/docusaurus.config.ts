import type * as Preset from '@docusaurus/preset-classic';
import type {Config} from '@docusaurus/types';
import {themes as prismThemes} from 'prism-react-renderer';

const organizationName = 'robertpopa22';
const projectName = 'mRemoteNG';

const config: Config = {
  title: 'mRemoteNG',
  tagline: 'The next generation of mRemote — open source, tabbed, multi-protocol remote connections manager',
  favicon: 'img/icon.png',

  // Published to GitHub Pages at https://robertpopa22.github.io/mRemoteNG/
  url: `https://${organizationName.toLowerCase()}.github.io`,
  baseUrl: `/${projectName}/`,
  organizationName,
  projectName,
  trailingSlash: false,

  onBrokenLinks: 'throw',
  markdown: {
    // Docusaurus 3 compiles .md as MDX by default, which makes JSX-looking text a
    // build error. The docs migrated from Sphinx contain literals such as
    // <user@domain>, <sftp://> and PowerShell hash literals like @{Name="x"} that
    // MDX cannot parse. 'detect' keeps .md on CommonMark and reserves MDX for .mdx.
    format: 'detect',
    hooks: {
      onBrokenMarkdownLinks: 'warn',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          // Docs-only mode: the docs are served from the site root.
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
          editUrl: `https://github.com/${organizationName}/${projectName}/tree/main/docs-website/`,
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themes: [
    [
      '@easyops-cn/docusaurus-search-local',
      {
        // Offline search: the index is built at compile time and served as
        // static assets, so it works on GitHub Pages with no search backend.
        hashed: true,
        // The site runs in docs-only mode (docs.routeBasePath === '/'), so the
        // indexer has to be pointed at the root too — it defaults to '/docs'
        // and would otherwise index nothing.
        docsRouteBasePath: '/',
        indexBlog: false,
        highlightSearchTermsOnTargetPage: true,
      },
    ],
  ],

  themeConfig: {
    image: 'img/logo.png',
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'mRemoteNG',
      logo: {
        alt: 'mRemoteNG logo',
        src: 'img/icon.png',
      },
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Docs',
        },
        {
          href: `https://github.com/${organizationName}/${projectName}/releases/latest`,
          label: 'Download',
          position: 'right',
        },
        {
          href: `https://github.com/${organizationName}/${projectName}`,
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Docs',
          items: [
            {label: 'Introduction', to: '/'},
            {label: 'User Interface', to: '/user-interface'},
            {label: 'Troubleshooting', to: '/troubleshooting'},
            {label: 'FAQ', to: '/faq'},
          ],
        },
        {
          title: 'Community',
          items: [
            {label: 'Reddit', href: 'https://reddit.com/r/mremoteng'},
            {label: 'Chat', href: 'https://gitter.im/mRemoteNG/PublicChat'},
            {label: 'Wiki', href: 'https://github.com/mRemoteNG/mRemoteNG/wiki'},
          ],
        },
        {
          title: 'More',
          items: [
            {label: 'GitHub', href: `https://github.com/${organizationName}/${projectName}`},
            {
              label: 'Issues',
              href: `https://github.com/${organizationName}/${projectName}/issues`,
            },
            {
              label: 'Releases',
              href: `https://github.com/${organizationName}/${projectName}/releases`,
            },
          ],
        },
      ],
      copyright: `Copyright © ${new Date().getFullYear()} The mRemoteNG Team. GPL-2.0 licensed.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'bash', 'powershell'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
