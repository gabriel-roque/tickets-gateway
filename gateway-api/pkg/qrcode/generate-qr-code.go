package qrcode

import (
	"fmt"
)

func GenerateQRCodePix(value float64, description string) string {
	qr_code_fake := fmt.Sprintf(
		"00020101021226580014BR.GOV.BCB.PIX0136+5581987654326014PIXFAKE202203520400005303986540%.2f5802BR5913PAYMENTS INC6011FAKE CITY62070503***%s",
		value, description)

	return qr_code_fake
}
