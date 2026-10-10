namespace DlssNrManager.Services;

/// <summary>
/// A short, bilingual diagnostic for a fatal UI error. Keep secrets and
/// untrusted exception messages out of the error dialog; details stay in
/// the redacted local log. A fatal error is not safe to swallow.
/// </summary>
public static class ApplicationFailureReport
{
    public static string Format(
        Exception exception,
        string module,
        string language,
        string logPath)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var french = UiLocalizationService.NormalizeLanguage(language) == "fr";
        var cause = exception switch
        {
            DllNotFoundException => french
                ? "Bibliothèque native manquante ou incompatible."
                : "Missing or incompatible native DLL.",
            FileNotFoundException => french
                ? "Fichier ou dépendance introuvable."
                : "A file or dependency is missing.",
            UnauthorizedAccessException => french
                ? "Accès refusé : vérifiez les autorisations et le dossier."
                : "Access denied: check permissions and the selected folder.",
            TimeoutException => french
                ? "Opération ou composant externe sans réponse."
                : "An operation or external component timed out.",
            HttpRequestException => french
                ? "Problème réseau ou service externe indisponible."
                : "Network error or external service unavailable.",
            OutOfMemoryException => french
                ? "Mémoire insuffisante."
                : "Not enough available memory.",
            _ => french
                ? "Erreur inattendue du module."
                : "Unexpected module failure."
        };
        return french
            ? $"ERREUR CRITIQUE\nCause probable : {cause}\nModule : {module}\nSolution : arrêtez les opérations en cours, consultez le journal et redémarrez l'application.\nLog : {logPath}"
            : $"CRITICAL ERROR\nProbable cause: {cause}\nModule: {module}\nSolution: stop ongoing operations, review the log and restart the application.\nLog: {logPath}";
    }
}
