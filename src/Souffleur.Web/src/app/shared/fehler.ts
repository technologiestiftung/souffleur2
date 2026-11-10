/**
 * Die Gründe aus einer ValidationProblem-Antwort des Servers.
 *
 * Eigene Datei, damit die Shell sie nutzen kann, ohne den Termindialog ins
 * Startpaket zu ziehen -- vorher kam sie von dort, samt allem, was er lädt.
 */
export function messagesFrom(error: unknown): string[] {
  const errors = (error as { error?: { errors?: Record<string, string[]> } })?.error?.errors;
  const found = errors ? Object.values(errors).flat() : [];
  return found.length > 0 ? found : ['Das hat nicht geklappt.'];
}
