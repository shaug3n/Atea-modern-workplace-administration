export type MessageKey = 'appTitle' | 'enableDarkMode' | 'disableDarkMode' | 'authSignInTitle' | 'authSignIn' | 'authSignInError' | 'authSignInRequired';

export const messages: Record<MessageKey, string> = {
  appTitle: 'Atea Unified Workplace',
  enableDarkMode: 'Enable dark mode',
  disableDarkMode: 'Disable dark mode',
  authSignInTitle: 'Sign in to Atea Unified Workplace',
  authSignIn: 'Sign in',
  authSignInError: 'Sign-in could not be started.',
  authSignInRequired: 'Sign-in is required',
};
