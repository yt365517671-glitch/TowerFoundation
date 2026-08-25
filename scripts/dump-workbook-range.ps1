param(
    [Parameter(Mandatory = $true)]
    [string]$WorkbookPath,

    [Parameter(Mandatory = $true)]
    [string]$WorksheetName,

    [Parameter(Mandatory = $true)]
    [string]$RangeAddress
)

$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path -LiteralPath $WorkbookPath).Path
$excel = $null
$workbook = $null

try {
    $excel = New-Object -ComObject Excel.Application
    $excel.Visible = $false
    $excel.DisplayAlerts = $false
    $excel.AskToUpdateLinks = $false
    $workbook = $excel.Workbooks.Open($resolvedPath, 0, $true)
    $worksheet = $workbook.Worksheets.Item($WorksheetName)
    $range = $worksheet.Range($RangeAddress)

    # Read the rectangular data in two COM calls. Iterating COM cells one by one
    # makes legacy calculation books with embedded objects take several minutes.
    $values = $range.Value2
    $formulas = $range.Formula
    $rowCount = [int]$range.Rows.Count
    $columnCount = [int]$range.Columns.Count
    $startRow = [int]$range.Row
    $startColumn = [int]$range.Column

    function Get-MatrixItem($matrix, [int]$row, [int]$column) {
        if ($rowCount -eq 1 -and $columnCount -eq 1) {
            return $matrix
        }
        return $matrix[$row, $column]
    }

    function ConvertTo-ExcelColumnName([int]$number) {
        $name = ''
        while ($number -gt 0) {
            $number--
            $name = [char](65 + ($number % 26)) + $name
            $number = [Math]::Floor($number / 26)
        }
        return $name
    }

    for ($row = 1; $row -le $rowCount; $row++) {
        for ($column = 1; $column -le $columnCount; $column++) {
            $value = Get-MatrixItem $values $row $column
            $formulaValue = Get-MatrixItem $formulas $row $column
            $formula = if ($formulaValue -is [string] -and
                $formulaValue.StartsWith('=')) {
                [string]$formulaValue
            } else {
                ''
            }
            if ($null -eq $value -and [string]::IsNullOrWhiteSpace($formula)) {
                continue
            }

            $address = "$(ConvertTo-ExcelColumnName ($startColumn + $column - 1))$($startRow + $row - 1)"
            [PSCustomObject]@{
                Cell = $address
                Value = [string]$value
                Formula = $formula
            }
        }
    }
}
finally {
    if ($workbook) {
        $workbook.Close($false)
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($workbook) | Out-Null
    }
    if ($excel) {
        $excel.Quit()
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($excel) | Out-Null
    }
    [GC]::Collect()
    [GC]::WaitForPendingFinalizers()
}
