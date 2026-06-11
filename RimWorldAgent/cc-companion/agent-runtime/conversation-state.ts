export type ConversationMessage<T> = T & { role: string };

export class ConversationState<T extends { role: string }> {
  private messages: T[] = [];
  private readonly lowMessages: number;
  private readonly lowChars: number;

  constructor(private readonly maxMessages: number, private readonly maxChars: number) {
    this.lowMessages = Math.max(1, Math.floor(maxMessages * 0.7));
    this.lowChars = Math.max(1, Math.floor(maxChars * 0.7));
  }

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
    let dropped = 0;
    if (this.messages.length > this.maxMessages) {
      while (this.messages.length > this.lowMessages && this.messages.length > 1) {
        this.messages.shift();
        dropped++;
      }
    }

    let chars = this.totalChars();
    if (chars > this.maxChars) {
      while (chars > this.lowChars && this.messages.length > 1) {
        this.messages.shift();
        dropped++;
        chars = this.totalChars();
      }
    }

    if (dropped > 0) {
      console.log(`[conversation-state] 历史压缩 dropped=${dropped} kept=${this.messages.length} chars=${chars}`);
    }
  }

  private totalChars(): number {
    return this.messages.reduce((sum, msg) => sum + JSON.stringify(msg).length, 0);
  }
}
