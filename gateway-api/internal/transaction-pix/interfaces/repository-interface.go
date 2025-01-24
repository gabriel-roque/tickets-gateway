package transaction_pix_interfaces

import (
	interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
)

type ITransactionPixRepository interface {
	Save(transaction *interfaces.CreateTransactionPix)
}
