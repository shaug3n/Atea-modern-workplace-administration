export type WebBuildMetadata = {
  productVersion: string;
  commit: string;
  branch: string;
};

const unavailable = 'Unavailable';

function buildValue(value: string | undefined): string {
  return value === undefined || value.trim() === '' ? unavailable : value;
}

export function readWebBuildMetadata(env: ImportMetaEnv): WebBuildMetadata {
  return {
    productVersion: buildValue(env.VITE_BUILD_PRODUCT_VERSION),
    commit: buildValue(env.VITE_BUILD_COMMIT),
    branch: buildValue(env.VITE_BUILD_BRANCH),
  };
}
