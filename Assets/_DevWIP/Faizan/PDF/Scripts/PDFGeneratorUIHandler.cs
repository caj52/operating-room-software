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

    public TMP_InputField inputProjectNumber;
    public TMP_InputField inputAccountName;
    public TMP_InputField inputAccountAddress;
    public TMP_InputField inputReferenceNumber;


    public TMP_InputField inputNote1;
    public TMP_InputField inputNote2;
    public TMP_InputField inputNote3;

    public TMP_InputField inputDiscountPercentage;

    private void Start()
    {
        pdfGenerator = FindAnyObjectByType<ProposalPDFGenerator>();
        // Add listener to the button
        btnGeneratePDF.onClick.AddListener(OnGeneratePdfButtonClicked);
        //setting the default values
        inputNote1.text = pdfGenerator.note1;
        inputNote2.text = pdfGenerator.note2;
        inputNote3.text = pdfGenerator.note3;
    }

    private void OnGeneratePdfButtonClicked()
    {
        // Set the input values to the ProposalPDFGenerator instance
        pdfGenerator.clientName = inputClientName.text;
        pdfGenerator.projectName = inputProjectName.text;
        pdfGenerator.salesRepName = inputSaleRepName.text;
        pdfGenerator.salesRepEmail = inputsalesRepEmail.text;

        pdfGenerator.projectNumber = inputProjectNumber.text;
        pdfGenerator.accountName = inputAccountName.text;
        pdfGenerator.accountAddress = inputAccountAddress.text;
        pdfGenerator.note1 = inputNote1.text;
        pdfGenerator.note2 = inputNote2.text;
        pdfGenerator.note3 = inputNote3.text;
        pdfGenerator.referenceNumber = inputReferenceNumber.text;

        pdfGenerator.discountPercentage = float.Parse(inputDiscountPercentage.text); // Set the discount percentage from the input field


        // Call the GeneratePDF method
        pdfGenerator.GeneratePDF();
    }
}
