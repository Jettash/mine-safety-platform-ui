let csrf: { headerName: string; token: string } | undefined

async function readError(response: Response): Promise<string> {
  try { return (await response.json()).message ?? `请求失败（${response.status}）` }
  catch { return `请求失败（${response.status}）` }
}

export async function ensureCsrf(force = false): Promise<{ headerName: string; token: string }> {
  if (!csrf || force) {
    const response = await fetch('/api/v1/auth/csrf', { credentials: 'same-origin' })
    if (!response.ok) throw new Error(await readError(response))
    csrf = await response.json()
  }
  return csrf!
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const method = (init.method ?? 'GET').toUpperCase()
  const headers = new Headers(init.headers)
  if (!['GET', 'HEAD', 'OPTIONS'].includes(method)) {
    const token = await ensureCsrf()
    headers.set(token.headerName, token.token)
  }
  if (init.body && typeof init.body === 'string' && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }
  const response = await fetch(path, { ...init, headers, credentials: 'same-origin' })
  if (response.status === 401) {
    window.dispatchEvent(new CustomEvent('session-expired'))
    throw new Error('登录已失效')
  }
  if (!response.ok) throw new Error(await readError(response))
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}
