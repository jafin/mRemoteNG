import type {SidebarsConfig} from '@docusaurus/plugin-content-docs';

// Mirrors the toctree structure of the Sphinx docs this content was migrated from
// (mRemoteNGDocumentation/index.rst): one category per toctree caption.
const sidebars: SidebarsConfig = {
  docsSidebar: [
    'introduction',
    {
      type: 'category',
      label: 'Application Handling',
      collapsed: false,
      items: [
        {
          type: 'category',
          label: 'User Interface',
          link: {type: 'doc', id: 'user-interface/index'},
          items: [
            'user-interface/main-window',
            'user-interface/panels',
            'user-interface/menu-container',
            'user-interface/connections',
            'user-interface/default-connection-properties',
            'user-interface/quick-connect',
            'user-interface/port-scan',
            'user-interface/screenshot-manager',
            'user-interface/notifications',
            'user-interface/import-export',
            'user-interface/ssh-file-transfer',
            'user-interface/external-tools',
            'user-interface/options',
          ],
        },
        'folders-and-inheritance',
        {
          type: 'category',
          label: 'Protocols',
          link: {type: 'doc', id: 'protocols/index'},
          items: ['protocols/anydesk', 'protocols/rdp'],
        },
        'keyboard-shortcuts',
        'connection-file-protection',
        'portable-edition',
        'sql-configuration',
        {
          type: 'category',
          label: 'Registry Settings',
          link: {type: 'doc', id: 'registry/index'},
          items: [
            'registry/registry-settings-information',
            'registry/startup-exit-settings',
            'registry/appearance-settings',
            'registry/connection-settings',
            'registry/tabs-panels-settings',
            'registry/notification-settings',
            'registry/credential-settings',
            'registry/sql-server-settings',
            'registry/updates-settings',
            'registry/security-settings',
          ],
        },
        'command-line-switches',
        {
          type: 'category',
          label: 'Themes',
          link: {type: 'doc', id: 'themes/index'},
          items: [
            'themes/darcula-ng',
            'themes/vs2012-blue',
            'themes/vs2012-dark',
            'themes/vs2012-light',
            'themes/vs2013-blue',
            'themes/vs2013-dark',
            'themes/vs2013-light',
            'themes/vs2015-blue',
            'themes/vs2015blue-ng',
            'themes/vs2015-dark',
            'themes/vs2015dark-ng',
            'themes/vs2015-light',
            'themes/vs2015light-ng',
          ],
        },
      ],
    },
    {
      type: 'category',
      label: 'Support',
      collapsed: false,
      items: ['troubleshooting', 'known-issues', 'faq'],
    },
    {
      type: 'category',
      label: 'HowTos',
      collapsed: false,
      items: [
        'howtos/sshtunnel',
        'howtos/external-tools',
        'howtos/bulk-connections',
        'howtos/vmrdp',
        'howtos/credvault',
        'howtos/dynamic-host',
        'howtos/cyberark-psm',
        'howtos/connection-frame-color',
      ],
    },
    {
      type: 'category',
      label: 'Miscellaneous',
      collapsed: false,
      items: [
        'variables-reference',
        'external-tools-cheat-sheet',
        'migrate',
        {
          type: 'link',
          label: 'Contribute',
          href: 'https://github.com/mRemoteNG/mRemoteNG/wiki',
        },
      ],
    },
    {
      type: 'category',
      label: 'Contact Us',
      collapsed: false,
      items: [
        'contact',
        {type: 'link', label: 'Chat', href: 'https://gitter.im/mRemoteNG/PublicChat'},
        {type: 'link', label: 'Reddit', href: 'https://reddit.com/r/mremoteng'},
      ],
    },
  ],
};

export default sidebars;
