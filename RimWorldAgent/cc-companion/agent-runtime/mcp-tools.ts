import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StreamableHTTPClientTransport } from '@modelcontextprotocol/sdk/client/streamableHttp.js';
import { CONFIG, errorMessage } from '../companion/config.js';
import type { AgentToolCall, AgentToolDefinition, AgentToolResult } from '../providers/types.js';

type McpListToolsResult = Awaited<ReturnType<Client['listTools']>>;
type McpTool = McpListToolsResult['tools'][number];
type McpCallToolResult = Awaited<ReturnType<Client['callTool']>>;

function toSchema(schema: unknown): Record<string, unknown> {
  if (schema && typeof schema === 'object' && !Array.isArray(schema)) return schema as Record<string, unknown>;
  return { type: 'object', properties: {} };
}

function contentToText(content: unknown): string {
  if (!Array.isArray(content)) return typeof content === 'string' ? content : JSON.stringify(content ?? '');
  return content.map((item: unknown) => {
    if (item && typeof item === 'object') {
      const obj = item as Record<string, unknown>;
      if (typeof obj.text === 'string') return obj.text;
      if (obj.resource && typeof obj.resource === 'object') {
        const resource = obj.resource as Record<string, unknown>;
        if (typeof resource.text === 'string') return resource.text;
      }
    }
    return JSON.stringify(item);
  }).join('\n').trim();
}

function toolToDefinition(tool: McpTool): AgentToolDefinition {
  return {
    name: String(tool.name),
    description: typeof tool.description === 'string' ? tool.description : '',
    inputSchema: toSchema(tool.inputSchema),
  };
}

function callResultContent(result: McpCallToolResult): unknown {
  if ('content' in result) return result.content;
  if ('toolResult' in result) return result.toolResult;
  return '';
}

function callResultIsError(result: McpCallToolResult): boolean {
  if ('isError' in result) return Boolean(result.isError);
  return false;
}

export class McpToolRuntime {
  private client: Client | undefined;
  private tools: AgentToolDefinition[] | undefined;
  private toolsLoadedAt = 0;

  constructor(private readonly toolCacheTtlMs = CONFIG.mcpToolCacheTtlMs) { }

  async listTools(): Promise<AgentToolDefinition[]> {
    if (this.tools && Date.now() - this.toolsLoadedAt < this.toolCacheTtlMs) return this.tools;
    try {
      const client = await this.getClient();
      const result = await client.listTools();
      this.tools = result.tools.map(toolToDefinition);
      this.toolsLoadedAt = Date.now();
      return this.tools;
    } catch (err: unknown) {
      await this.reset();
      const client = await this.getClient();
      const result = await client.listTools();
      this.tools = result.tools.map(toolToDefinition);
      this.toolsLoadedAt = Date.now();
      return this.tools;
    }
  }

  async callTool(call: AgentToolCall): Promise<AgentToolResult> {
    try {
      const client = await this.getClient();
      const result = await client.callTool({
        name: call.name,
        arguments: toArguments(call.input),
      });
      return {
        id: call.id,
        name: call.name,
        content: contentToText(callResultContent(result)),
        isError: callResultIsError(result),
      };
    } catch (err: unknown) {
      await this.reset();
      return {
        id: call.id,
        name: call.name,
        content: `工具 ${call.name} 执行失败: ${errorMessage(err)}`,
        isError: true,
      };
    }
  }

  private async getClient(): Promise<Client> {
    if (this.client) return this.client;
    const client = new Client({ name: 'rimworld-agent-provider-runtime', version: '1.0.0' }, { capabilities: {} });
    const transport = new StreamableHTTPClientTransport(new URL(CONFIG.agentMcpUrl));
    await client.connect(transport);
    this.client = client;
    return client;
  }

  private async reset(): Promise<void> {
    const client = this.client;
    this.client = undefined;
    this.tools = undefined;
    this.toolsLoadedAt = 0;
    if (client) {
      try { await client.close(); }
      catch { }
    }
  }
}

function toArguments(input: unknown): Record<string, unknown> {
  if (input && typeof input === 'object' && !Array.isArray(input)) return input as Record<string, unknown>;
  return {};
}
