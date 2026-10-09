import React, { useEffect, useRef, useState } from 'react';
import { useApi } from '../../auth/useApi';
import { WorkspacePageHeader } from '../../components/WorkspacePageHeader';
import { messages } from '../../app/messages';
import { formatDate } from '../../format/dateTime';
import { listFeedback, type FeedbackSubmission } from './feedbackApi';
import { feedbackErrorMessage } from './feedbackErrorMessage';
import './feedback.css';

export function FeedbackPage({ refreshRevision, onOpenFeedbackDialog }: { refreshRevision: number; onOpenFeedbackDialog: () => void }) {
  const api = useApi();
  const sequence = useRef(0);
  const [items, setItems] = useState<FeedbackSubmission[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    const requestId = ++sequence.current;
    let active = true;
    setItems([]);
    setNextCursor(null);
    setLoading(true);
    setLoadingMore(false);
    setError('');
    listFeedback(api).then(page => {
      if (!active || requestId !== sequence.current) return;
      setItems(page.items);
      setNextCursor(page.nextCursor);
    }).catch(reason => {
      if (active && requestId === sequence.current) setError(feedbackErrorMessage(reason, 'load'));
    }).finally(() => {
      if (active && requestId === sequence.current) setLoading(false);
    });
    return () => {
      active = false;
      sequence.current += 1;
    };
  }, [api, refreshRevision]);

  const loadMore = () => {
    if (!nextCursor || loadingMore) return;
    const requestId = ++sequence.current;
    setLoadingMore(true);
    setError('');
    listFeedback(api, nextCursor).then(page => {
      if (requestId !== sequence.current) return;
      setItems(current => [...current, ...page.items]);
      setNextCursor(page.nextCursor);
    }).catch(reason => {
      if (requestId === sequence.current) setError(feedbackErrorMessage(reason, 'load'));
    }).finally(() => {
      if (requestId === sequence.current) setLoadingMore(false);
    });
  };

  return (
    <section className="feedback-page" aria-labelledby="page-title">
      <WorkspacePageHeader title={messages.feedbackTitle} description={messages.feedbackDescription} />
      <div className="feedback-page__actions">
        <button className="button button--primary" type="button" onClick={onOpenFeedbackDialog}>{messages.feedbackGiveAction}</button>
      </div>
      {loading && <p className="feedback-state" role="status">{messages.feedbackPageLoading}</p>}
      {!loading && error && <p className="feedback-state feedback-state--error" role="alert">{error}</p>}
      {!loading && !error && items.length === 0 && (
        <section className="feedback-empty" aria-labelledby="feedback-empty-title">
          <h2 id="feedback-empty-title">{messages.feedbackEmptyTitle}</h2>
          <p>{messages.feedbackEmptyBody}</p>
          <button className="button button--primary" type="button" onClick={onOpenFeedbackDialog}>{messages.feedbackGiveAction}</button>
        </section>
      )}
      {!loading && items.length > 0 && (
        <section className="feedback-list" aria-label={messages.feedbackTitle}>
          {items.map(item => (
            <article className="feedback-card" key={item.id}>
              <div className="feedback-card__meta">
                <span className="feedback-card__category">{item.category}</span>
                <time dateTime={item.createdAt}>{messages.feedbackCreatedAt} · {formatDate(item.createdAt)}</time>
              </div>
              <h2 data-feedback-subject>{item.subject}</h2>
              <p className="feedback-card__message">{item.message}</p>
            </article>
          ))}
          {nextCursor && <button className="button button--secondary" type="button" disabled={loadingMore} onClick={loadMore}>{loadingMore ? messages.feedbackLoadingMore : messages.feedbackLoadMore}</button>}
        </section>
      )}
    </section>
  );
}
