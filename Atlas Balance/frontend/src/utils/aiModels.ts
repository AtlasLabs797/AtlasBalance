export interface AiModelOption {
  value: string;
  label: string;
}

export const OPENROUTER_AUTO_MODEL = 'openrouter/auto';
export const OPENROUTER_DEFAULT_RUNTIME_MODEL = 'nvidia/nemotron-3-super-120b-a12b:free';
export const DEFAULT_OPENAI_MODEL = 'gpt-4o-mini';
export const DEFAULT_MINIMAX_MODEL = 'MiniMax-M3';

export const aiProviderOptions: AiModelOption[] = [
  { value: 'OPENROUTER', label: 'OpenRouter' },
  { value: 'OPENAI', label: 'OpenAI' },
  { value: 'MINIMAX', label: 'MiniMax' },
];

export const openRouterModelOptions: AiModelOption[] = [
  { value: OPENROUTER_AUTO_MODEL, label: 'OpenRouter Auto' },
  { value: OPENROUTER_DEFAULT_RUNTIME_MODEL, label: 'Nemotron 3 Super (free)' },
  { value: 'google/gemma-4-31b-it:free', label: 'Gemma 4 31B (free)' },
  { value: 'minimax/minimax-m2.5:free', label: 'MiniMax M2.5 (free)' },
  { value: 'openai/gpt-oss-120b:free', label: 'gpt-oss-120b (free)' },
  { value: 'z-ai/glm-4.5-air:free', label: 'GLM 4.5 Air (free)' },
  { value: 'qwen/qwen3-coder:free', label: 'Qwen3 Coder 480B A35B (free)' },
];

export const openAiModelOptions: AiModelOption[] = [
  { value: 'gpt-4.1-mini', label: 'GPT-4.1 mini' },
  { value: 'gpt-4o-mini', label: 'GPT-4o mini' },
  { value: 'gpt-4o', label: 'GPT-4o' },
];

export const miniMaxModelOptions: AiModelOption[] = [
  { value: DEFAULT_MINIMAX_MODEL, label: 'MiniMax M3' },
  { value: 'MiniMax-M2.7', label: 'MiniMax M2.7' },
];

export function normalizeAiProvider(provider: string | null | undefined) {
  if (provider === 'OPENAI' || provider === 'MINIMAX') {
    return provider;
  }

  return 'OPENROUTER';
}

export function getAiModelOptions(provider: string | null | undefined) {
  const normalizedProvider = normalizeAiProvider(provider);
  if (normalizedProvider === 'OPENAI') {
    return openAiModelOptions;
  }

  return normalizedProvider === 'MINIMAX' ? miniMaxModelOptions : openRouterModelOptions;
}

export function getDefaultAiModel(provider: string | null | undefined) {
  const normalizedProvider = normalizeAiProvider(provider);
  if (normalizedProvider === 'OPENAI') {
    return DEFAULT_OPENAI_MODEL;
  }

  return normalizedProvider === 'MINIMAX' ? DEFAULT_MINIMAX_MODEL : OPENROUTER_AUTO_MODEL;
}

export function normalizeAiModel(provider: string | null | undefined, model: string | null | undefined) {
  const trimmed = model?.trim() ?? '';
  const normalizedProvider = normalizeAiProvider(provider);
  if (normalizedProvider === 'OPENROUTER') {
    return trimmed || OPENROUTER_AUTO_MODEL;
  }

  const options = normalizedProvider === 'OPENAI' ? openAiModelOptions : miniMaxModelOptions;
  return options.some((item) => item.value === trimmed) ? trimmed : getDefaultAiModel(normalizedProvider);
}

export function getAiModelLabel(provider: string | null | undefined, model: string | null | undefined) {
  const normalizedModel = normalizeAiModel(provider, model);
  return getAiModelOptions(provider).find((item) => item.value === normalizedModel)?.label ?? normalizedModel;
}

export function isValidOpenRouterModelId(model: string | null | undefined) {
  const trimmed = model?.trim() ?? '';
  if (trimmed.length < 3 || trimmed.length > 160) {
    return false;
  }

  if (trimmed.includes('..') || trimmed.includes('//') || trimmed.startsWith('/') || trimmed.endsWith('/')) {
    return false;
  }

  return /^[A-Za-z0-9/_:.\-+]+$/.test(trimmed);
}
