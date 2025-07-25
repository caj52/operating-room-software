using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PricingRowDataFill : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] public TMP_Text partNo;
    [SerializeField] public TMP_Text modelName;
    [SerializeField] public TMP_Text quantity;
    [SerializeField] public TMP_Text listPrice;

    [Header("Data")]
    [SerializeField] private double price;
    [SerializeField] private int currentQuantity = 1;

    public SelectablePrice associatedObject { get; private set; }

    // Events
    public event Action<PricingRowDataFill> OnRowDestroyed;
    public event Action<PricingRowDataFill> OnQuantityChanged;
    public event Action<PricingRowDataFill> OnPriceUpdated;

    // Properties
    public double Price => price;
    public int Quantity => currentQuantity;
    public double TotalPrice => price * currentQuantity;

    #region Initialization
    /// <summary>
    /// Initializes the pricing row with data
    /// </summary>
    public void Initialize(PriceExcelData data, int quantityValue, SelectablePrice primaryObject)
    {
        if (data == null || primaryObject == null)
        {
            Debug.LogError("Cannot initialize PricingRowDataFill with null data or object");
            return;
        }

        associatedObject = primaryObject;
        currentQuantity = quantityValue;

        // Update UI
        UpdateUI(data);

        // Link back to the SelectablePrice

        // Subscribe to updates
        SubscribeToPriceUpdates(data);
        primaryObject.UIRefPricingRowDataFill = this;
    }

    /// <summary>
    /// Updates the row with new pricing data
    /// </summary>
    public void UpdateData(PriceExcelData data, int? newQuantity = null)
    {
        if (data == null) return;

        if (newQuantity.HasValue)
        {
            currentQuantity = newQuantity.Value;
        }

        UpdateUI(data);
        Debug.LogError("++" + data.ListPrice);
        OnPriceUpdated?.Invoke(this);
    }
    #endregion

    #region UI Updates
    private void UpdateUI(PriceExcelData data)
    {
        // Update text fields
        if (partNo != null) partNo.text = data.PartNumber ?? "N/A";
        if (modelName != null) modelName.text = data.ObjectName ?? "Unknown";
        if (quantity != null) quantity.text = currentQuantity.ToString();
        // Calculate total price
        double totalPrice = CalculateTotalPrice(data);
        price = totalPrice;

        // Update price display
        if (listPrice != null) listPrice.text = $"${totalPrice:F2}";

        // Store the calculated price in the associated object
        if (associatedObject != null)
        {
            associatedObject.Price = totalPrice.ToString("F2");
        }
    }

    private double CalculateTotalPrice(PriceExcelData data)
    {
        double basePrice = data.ListPrice;

        if (data.isSimFlexArmAvailable)
        {
            basePrice += data.SimFlexPrice;
        }

        return basePrice;
    }
    #endregion

    #region Quantity Management
    public void SetQuantity(int newQuantity)
    {
        if (newQuantity < 1) newQuantity = 1;

        if (currentQuantity != newQuantity)
        {
            currentQuantity = newQuantity;

            if (quantity != null)
            {
                quantity.text = currentQuantity.ToString();
            }

            OnQuantityChanged?.Invoke(this);
            OnPriceUpdated?.Invoke(this);
        }
    }

    public void IncrementQuantity()
    {
        SetQuantity(currentQuantity + 1);
    }

    public void DecrementQuantity()
    {
        SetQuantity(currentQuantity - 1);
    }
    #endregion

    #region Event Subscriptions
    private void SubscribeToPriceUpdates(PriceExcelData data)
    {

        UpdateData(data);

    }

    private void UnsubscribeFromPriceUpdates()
    {
        // Clean up any event subscriptions
    }
    #endregion

    #region Lifecycle
    public void DestroyRow()
    {
        // Notify listeners
        OnRowDestroyed?.Invoke(this);

        // Notify the UI system

        Debug.Log($"Destroying price row for {modelName?.text}");

        // Clean up
        UnsubscribeFromPriceUpdates();

        // Destroy after a frame to allow UI updates
        Destroy(gameObject, 0.1f);
    }

    private void OnDestroy()
    {
        OnRowDestroyed?.Invoke(this);
        UnsubscribeFromPriceUpdates();

        // Clear reference in associated object
        if (associatedObject != null)
        {
            associatedObject.UIRefPricingRowDataFill = null;
        }
    }
    #endregion

    #region Utility Methods
    /// <summary>
    /// Gets a formatted string representation of this pricing row
    /// </summary>
    public string GetFormattedSummary()
    {
        return $"{modelName?.text ?? "Unknown"} (x{currentQuantity}) - ${TotalPrice:F2}";
    }

    /// <summary>
    /// Exports this row's data as a CSV line
    /// </summary>
    public string ExportAsCSV()
    {
        return $"{partNo?.text},{modelName?.text},{currentQuantity},{price:F2},{TotalPrice:F2}";
    }

    /// <summary>
    /// Validates that all required UI elements are connected
    /// </summary>
    public bool ValidateUIReferences()
    {
        bool isValid = true;

        if (partNo == null)
        {
            Debug.LogError($"Part number text is not assigned on {gameObject.name}");
            isValid = false;
        }

        if (modelName == null)
        {
            Debug.LogError($"Model name text is not assigned on {gameObject.name}");
            isValid = false;
        }

        if (quantity == null)
        {
            Debug.LogError($"Quantity text is not assigned on {gameObject.name}");
            isValid = false;
        }

        if (listPrice == null)
        {
            Debug.LogError($"List price text is not assigned on {gameObject.name}");
            isValid = false;
        }

        return isValid;
    }
    #endregion
}