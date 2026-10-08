using System.Diagnostics;
using System.Windows;
using DlssNrManager.Services;

namespace DlssNrManager.Dialogs;

public partial class AiStudioLicenseDialog : Window
{
    private readonly AiStudioLicenseInfo _info;

    public bool Accepted { get; private set; }

    public AiStudioLicenseDialog(
        AiStudioLicenseInfo info,
        string language)
    {
        InitializeComponent();

        _info = info;
        var french =
            UiLocalizationService.NormalizeLanguage(language) == "fr";

        Title = french ? "Licence du modèle" : "Model license";
        ModelNameText.Text = info.ModelName;
        LicenseNameText.Text = info.LicenseName;
        SummaryText.Text =
            french
                ? info.SummaryFrench
                : info.SummaryEnglish;

        LegalNoticeText.Text = french
            ? "Avant d'installer ce modèle, vous devez consulter et accepter sa licence officielle.\n\nDLSS NR Manager enregistre seulement votre acceptation locale. Cette acceptation ne modifie pas la licence, ne vous accorde aucun droit supplémentaire et n'autorise pas automatiquement DLSS NR Manager à redistribuer les poids du modèle.\n\nPour les modèles gated ou sous licence restreinte, vous devez obtenir les fichiers depuis la source officielle conformément à ses conditions, puis les importer dans le stockage local géré par l'application."
            : "Before installing this model, you must review and accept its official license.\n\nDLSS NR Manager only records your local acceptance. This acceptance does not change the license, grant additional rights, or automatically authorize DLSS NR Manager to redistribute the model weights.\n\nFor gated or restricted-license models, obtain the files from the official source under its terms, then import them into the managed local storage.";

        AcceptCheckBox.Content = french
            ? "J'ai consulté la licence officielle et j'accepte ses conditions."
            : "I have reviewed the official license and accept its terms.";

        OpenLicenseButton.Content =
            french ? "Voir la licence officielle" : "View official license";
        DeclineButton.Content =
            french ? "Refuser" : "Decline";
        AcceptButton.Content =
            french ? "Accepter" : "Accept";
    }

    private void OpenLicense_Click(
        object sender,
        RoutedEventArgs e)
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName = _info.OfficialLicenseUrl,
                UseShellExecute = true
            });
    }

    private void AcceptCheckBox_Changed(
        object sender,
        RoutedEventArgs e)
    {
        AcceptButton.IsEnabled =
            AcceptCheckBox.IsChecked == true;
    }

    private void Accept_Click(
        object sender,
        RoutedEventArgs e)
    {
        Accepted = true;
        DialogResult = true;
        Close();
    }

    private void Decline_Click(
        object sender,
        RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
        Close();
    }
}
