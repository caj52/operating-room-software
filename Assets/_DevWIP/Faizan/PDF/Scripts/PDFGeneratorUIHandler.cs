using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PDFGeneratorUIHandler : MonoBehaviour
{
    public TMP_InputField inputClientName;
    public TMP_InputField inputProjectName;
    public TMP_InputField inputSaleRepName
        ;
    public TMP_InputField inputsalesRepEmail;
    public Button btnGeneratePDF;
    public ProposalPDFGenerator pdfGenerator;

    private void Start()
    {
        pdfGenerator = FindAnyObjectByType<ProposalPDFGenerator>();
        // Add listener to the button
        btnGeneratePDF.onClick.AddListener(OnGeneratePdfButtonClicked);
    }

    private void OnGeneratePdfButtonClicked()
    {
        // Set the input values to the ProposalPDFGenerator instance
        pdfGenerator.clientName = inputClientName.text;
        pdfGenerator.projectName = inputProjectName.text;
        pdfGenerator.salesRepName = inputSaleRepName.text;
        pdfGenerator.salesRepEmail = inputsalesRepEmail.text;

        // Call the GeneratePDF method
        pdfGenerator.GeneratePDF();
    }
}
