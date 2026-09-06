export const abortError = () => Object.assign(new Error("The operation was cancelled."), { name: "AbortError" });

/** Stop waiting even when an underlying renderer does not implement cancellation. */
export const abortable = <T>(operation: Promise<T>, signal?: AbortSignal): Promise<T> => {
  if (!signal) return operation;
  if (signal.aborted) return Promise.reject(abortError());
  return new Promise<T>((resolve, reject) => {
    const cancel = () => reject(abortError());
    signal.addEventListener("abort", cancel, { once: true });
    operation.then(resolve, reject).finally(() => signal.removeEventListener("abort", cancel));
  });
};
