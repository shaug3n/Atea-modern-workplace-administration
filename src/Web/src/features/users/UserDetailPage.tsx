import React, { useEffect, useMemo, useState } from 'react';
import { messages } from '../../app/messages';
import { useApi } from '../../auth/useApi';
import { AsyncState } from '../../components/AsyncState';
import { GroupsSection } from './GroupsSection';
import { IdentitySection } from './IdentitySection';
import { JobInformationSection } from './JobInformationSection';
import { LicensesSection } from './LicensesSection';
import { RolesAndPimSection } from './RolesAndPimSection';
import { fetchUserDetail, type ApiFetch, type UserDetailResponse } from './userDetailApi';

export function UserDetailPage({ userId, loadUserDetail }: { userId?: string; loadUserDetail?: (userId: string) => Promise<UserDetailResponse> }) {
  const api = useApi();
  const resolvedUserId = userId ?? userIdFromPath(window.location.pathname);
  const loader = useMemo(() => loadUserDetail ?? ((id: string) => fetchUserDetail(api as ApiFetch, id)), [api, loadUserDetail]);
  const [detail, setDetail] = useState<UserDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    loader(resolvedUserId)
      .then((response) => {
        if (!cancelled) setDetail(response);
      })
      .catch((loadError: unknown) => {
        if (!cancelled) setError(loadError instanceof Error ? loadError : new Error('user_detail_unavailable'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [loader, resolvedUserId]);

  if (loading) {
    return <AsyncState state="loading"><span>{messages.userDetailLoading}</span></AsyncState>;
  }

  if (error) {
    return (
      <section className="permission-panel" role="alert">
        <h1>{messages.userDetailTitle}</h1>
        <p>{error.message === 'user_not_found' ? messages.userDetailNotFound : messages.userDetailUnavailable}</p>
      </section>
    );
  }

  if (!detail?.user) {
    return (
      <section className="permission-panel" role="status">
        <h1>{messages.userDetailTitle}</h1>
        <p>{detail?.access.error?.message ?? messages.userDetailUnavailable}</p>
      </section>
    );
  }

  return (
    <section className="user-detail-page" aria-labelledby="user-detail-title">
      <div className="users-page__header">
        <div>
          <p className="eyebrow">{messages.userDetailTitle}</p>
          <h1 id="user-detail-title">{detail.user.displayName || detail.user.userPrincipalName || messages.usersUnnamedUser}</h1>
          <p>{detail.user.userPrincipalName || messages.usersUnavailableValue}</p>
        </div>
      </div>
      <div className="detail-grid">
        <IdentitySection user={detail.user} access={detail.access} />
        <JobInformationSection user={detail.user} access={detail.access} />
        <LicensesSection section={detail.licenses} />
        <GroupsSection section={detail.groups} />
        <RolesAndPimSection roles={detail.roles} pim={detail.pim} />
      </div>
    </section>
  );
}

function userIdFromPath(pathname: string) {
  const [, usersSegment, userId] = pathname.split('/');
  return usersSegment === 'users' && userId ? decodeURIComponent(userId) : '';
}
