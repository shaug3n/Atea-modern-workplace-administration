import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { CAPABILITY_LABELS, humanizeActivityAction, humanizeAssignmentState, humanizeAuthMethodType, humanizeCapability } from '../../../src/Web/src/format/humanize';

describe('humanize helpers', () => {
  it('labels required capabilities and falls back for unknown keys', () => {
    expect(humanizeCapability('users.update')).toBe('Edit users');
    expect(humanizeCapability('users.reset_password')).toBe('Reset passwords');
    expect(humanizeCapability('devices.privileged.manage')).toBe('Manage devices (remote actions)');
    expect(humanizeCapability('authentication.campaigns.view')).toBe('View authentication campaigns');
    expect(humanizeCapability('authentication.campaigns.manage')).toBe('Manage authentication campaigns');
    expect(humanizeCapability('licenses.hygiene.view')).toBe('View license hygiene');
    expect(humanizeCapability('platform.about.view')).toBe('View system information');
    expect(humanizeCapability('feedback.submit')).toBe('Submit feedback');
    expect(humanizeCapability('totally.unknown')).toBe('Additional permission');
  });

  it('maps every Capability member', () => {
    const source = readFileSync('../../src/Web/src/capabilities/capabilityTypes.ts', 'utf8');
    const block = source.slice(source.indexOf('export type Capability'), source.indexOf('export const workspaceSettingsCapability'));
    const keys = [...block.matchAll(/'([a-z_.]+)'/g)].map((match) => match[1]);
    expect(keys.length).toBeGreaterThan(10);
    for (const key of keys) expect(Object.keys(CAPABILITY_LABELS)).toContain(key);
  });

  it('humanizes authentication method types', () => {
    expect(humanizeAuthMethodType('passwordAuthenticationMethod')).toBe('Password');
    expect(humanizeAuthMethodType('#microsoft.graph.fido2AuthenticationMethod')).toBe('Passkey (FIDO2)');
    expect(humanizeAuthMethodType('MicrosoftAuthenticatorAuthenticationMethod')).toBe('Microsoft Authenticator');
    expect(humanizeAuthMethodType('windowsHelloForBusinessAuthenticationMethod')).toBe('Windows Hello for Business');
    expect(humanizeAuthMethodType('temporaryAccessPassAuthenticationMethod')).toBe('Temporary Access Pass');
    expect(humanizeAuthMethodType('somethingElse')).toBe('Other method');
    expect(humanizeAuthMethodType(undefined)).toBe('Other method');
  });

  it('humanizes assignment states without leaking raw keys', () => {
    expect(humanizeAssignmentState('eligible')).toEqual({ label: 'Eligible', tone: 'info' });
    expect(humanizeAssignmentState('active')).toEqual({ label: 'Active', tone: 'success' });
    const unknown = humanizeAssignmentState('weird_state');
    expect(unknown.tone).toBe('neutral');
    expect(unknown.label).not.toBe('weird_state');
  });

  it('humanizes activity actions', () => {
    expect(humanizeActivityAction('users.update')).toBe('Updated user');
    expect(humanizeActivityAction('foo.bar_baz')).toBe('Foo bar baz');
  });
});
