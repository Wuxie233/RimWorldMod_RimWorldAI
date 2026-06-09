export type ConversationMessage<T> = T & { role: string };

export class ConversationState<T extends { role: string }> {
  private messages: T[] = [];

  constructor(private readonly maxMessages: number, private readonly maxChars: number) { }

  getHistory(): T[] {
    return [...this.messages];
  }

  append(...messages: T[]): void {
    this.messages.push(...messages);
    this.trim();
  }

  clear(): void {
    this.messages = [];
  }

  private trim(): void {
    while (this.messages.length > this.maxMessages) this.messages.shift();
    while (this.totalChars() > this.maxChars && this.messages.length > 1) this.messages.shift();
  }

  private totalChars(): number {
    return this.messages.reduce((sum, msg) => sum + JSON.stringify(msg).length, 0);
  }
}
