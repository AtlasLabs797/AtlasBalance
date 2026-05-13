export interface AiModelOption {
  value: string;
  label: string;
}

export const OPENROUTER_AUTO_MODEL = 'openrouter/auto';
export const OPENROUTER_DEFAULT_RUNTIME_MODEL = 'nvidia/nemotron-3-super-120b-a12b:free';
export const DEFAULT_OPENAI_MODEL = 'gpt-4o-mini';

export const aiProviderOptions: AiModelOption[] = [
  { value: 'OPENROUTER', label: 'OpenRouter' },
  { value: 'OPENAI', label: 'OpenAI' },
];

export const openRouterModelOptions: AiModelOption[] = [
  { value: OPENROUTER_AUTO_MODEL, label: 'Auto (gratis permitido)' },
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

export function normalizeAiProvider(provider: string | null | undefined) {
  return provider === 'OPENAI' ? 'OPENAI' : 'OPENROUTER';
}

export function getAiModelOptions(provider: string | null | undefined) {
  return normalizeAiProvider(provider) === 'OPENAI' ? openAiModelOptions : openRouterModelOptions;
}

export function getDefaultAiModel(provider: string | null | undefined) {
  return normalizeAiProvider(provider) === 'OPENAI' ? DEFAULT_OPENAI_MODEL : OPENROUTER_AUTO_MODEL;
}

export function normalizeAiModel(provider: string | null | undefined, model: string | null | undefined) {
  const trimmed = model?.trim() ?? '';
  return getAiModelOptions(provider).some((item) => item.value === trimmed)
    ? trimmed
    : getDefaultAiModel(provider);
}

export function getAiModelLabel(provider: string | null | undefined, model: string | null | undefined) {
  const normalizedModel = normalizeAiModel(provider, model);
  return getAiModelOptions(provider).find((item) => item.value === normalizedModel)?.label ?? normalizedModel;
}
