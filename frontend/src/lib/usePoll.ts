"use client";

import { useCallback, useEffect, useRef, useState } from "react";

export interface UsePollOptions<T> {
  fn: () => Promise<T>;
  intervalMs: number;
  /** Set false to pause polling entirely — e.g. while a modal edit is in flight. Default true. */
  enabled?: boolean;
  /** Skip ticks while the tab is hidden, so a backgrounded tab doesn't keep hammering the API. Default true. */
  pauseWhenHidden?: boolean;
  /** Refetch immediately when the tab/window regains focus, on top of the interval. Default true. */
  refreshOnFocus?: boolean;
  /** Runs after a successful fetch, e.g. to conditionally merge the result into other state. */
  onSuccess?: (data: T) => void;
  onError?: (e: unknown) => void;
  /** Consecutive-error backoff so a degraded backend isn't hammered; resets to intervalMs on the next success. */
  backoff?: { multiplier: number; maxMs: number };
}

export interface UsePollResult<T> {
  data: T | null;
  error: unknown;
  loading: boolean;
  /** Manual refetch — also resets any accumulated backoff delay. */
  refresh: () => Promise<void>;
}

// Consolidates the setInterval + visibility/focus-gating pattern already hand-rolled across the admin
// dashboard (useMe, cluster/chat pages, LiveChat, ServerStatus) into one hook, for NEW call-sites that
// currently have no live refresh at all. Existing call-sites are left as-is on purpose — see the live-data
// plan for why migrating them isn't worth the regression risk on a live site.
export function usePoll<T>(opts: UsePollOptions<T>): UsePollResult<T> {
  const { fn, intervalMs, enabled = true, pauseWhenHidden = true, refreshOnFocus = true, onSuccess, onError, backoff } = opts;

  const [data, setData] = useState<T | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(true);

  // Callbacks are read through refs inside the interval closure so a re-render (e.g. from setData itself)
  // never forces the interval to be torn down and rebuilt — that would reset any accumulated backoff. The
  // refs are synced in an effect (post-render), not during render, since mutating a ref while rendering
  // breaks React's render-must-be-pure contract.
  const fnRef = useRef(fn);
  const onSuccessRef = useRef(onSuccess);
  const onErrorRef = useRef(onError);
  useEffect(() => {
    fnRef.current = fn;
    onSuccessRef.current = onSuccess;
    onErrorRef.current = onError;
  });

  const delayRef = useRef(intervalMs);
  const failuresRef = useRef(0);

  const tick = useCallback(async () => {
    try {
      const result = await fnRef.current();
      setData(result);
      setError(null);
      failuresRef.current = 0;
      delayRef.current = intervalMs;
      onSuccessRef.current?.(result);
    } catch (e) {
      setError(e);
      onErrorRef.current?.(e);
      if (backoff) {
        failuresRef.current += 1;
        delayRef.current = Math.min(intervalMs * backoff.multiplier ** failuresRef.current, backoff.maxMs);
      }
    } finally {
      setLoading(false);
    }
  }, [intervalMs, backoff]);

  const refresh = useCallback(async () => {
    delayRef.current = intervalMs;
    failuresRef.current = 0;
    await tick();
  }, [tick, intervalMs]);

  useEffect(() => {
    if (!enabled) return;

    let alive = true;
    let timer: ReturnType<typeof setTimeout>;

    const shouldSkip = () => pauseWhenHidden && document.visibilityState === "hidden";

    const run = async () => {
      if (!alive) return;
      if (!shouldSkip()) await tick();
      if (!alive) return;
      timer = setTimeout(run, delayRef.current);
    };

    run();

    const onFocus = () => {
      if (refreshOnFocus && alive) tick();
    };
    if (refreshOnFocus) {
      window.addEventListener("focus", onFocus);
      document.addEventListener("visibilitychange", onFocus);
    }

    return () => {
      alive = false;
      clearTimeout(timer);
      if (refreshOnFocus) {
        window.removeEventListener("focus", onFocus);
        document.removeEventListener("visibilitychange", onFocus);
      }
    };
  }, [enabled, pauseWhenHidden, refreshOnFocus, tick]);

  return { data, error, loading, refresh };
}
