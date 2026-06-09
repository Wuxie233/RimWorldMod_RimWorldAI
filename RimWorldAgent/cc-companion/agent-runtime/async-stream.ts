export class AsyncStream<T = unknown> implements AsyncIterator<T> {
  private queue: T[] = [];
  private readResolve: ((v: IteratorResult<T>) => void) | undefined;
  private isDone = false;
  private started = false;

  [Symbol.asyncIterator](): AsyncIterator<T> {
    if (this.started) throw new Error('Stream can only be iterated once');
    this.started = true;
    return this;
  }

  async next(): Promise<IteratorResult<T>> {
    if (this.queue.length > 0) return { done: false, value: this.queue.shift()! };
    if (this.isDone) return { done: true, value: undefined as T };
    return new Promise((resolve) => { this.readResolve = resolve; });
  }

  enqueue(value: T): void {
    if (this.isDone) return;
    if (this.readResolve) {
      const resolve = this.readResolve;
      this.readResolve = undefined;
      resolve({ done: false, value });
    } else {
      this.queue.push(value);
    }
  }

  done(): void {
    this.isDone = true;
    if (this.readResolve) {
      const resolve = this.readResolve;
      this.readResolve = undefined;
      resolve({ done: true, value: undefined as T });
    }
  }
}
