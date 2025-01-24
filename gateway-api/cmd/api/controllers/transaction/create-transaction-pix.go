package controllers

import (
	"net/http"

	transaction_pix_handlers "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/handlers"
	transaction_interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
	"github.com/gin-gonic/gin"
)

func CreateTransactionPix(ctx *gin.Context) {
	var body transaction_interfaces.CreateTransactionPix

	if err := ctx.ShouldBindJSON(&body); err != nil {
		ctx.AbortWithStatusJSON(http.StatusBadRequest,
			gin.H{
				"success": false,
				"error":   err.Error(),
			})
		return
	}

	handler := transaction_pix_handlers.NewTransactionPixHandler()
	handler.Create(body)

	ctx.JSON(http.StatusCreated, gin.H{"success": true})
}
