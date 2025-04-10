package main

import (
	controllers "github.com/gabriel-roque/gateway-api/cmd/api/controllers/transaction"
	"github.com/gabriel-roque/gateway-api/pkg/middlewares"
	"github.com/gin-gonic/gin"
)

func TransactionPixRoutes(router *gin.Engine) {
	transactionPixRoutes := router.Group("/transaction-pix")

	transactionPixRoutes.Use(middlewares.ApiKeyMiddleware())

	transactionPixRoutes.POST("/", controllers.CreateTransactionPix)
	transactionPixRoutes.GET("/:id", controllers.GetTransactionPix)
}
