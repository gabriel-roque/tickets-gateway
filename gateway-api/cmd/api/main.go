package main

import (
	"log"

	"github.com/gabriel-roque/gateway-api/pkg/database"
	"github.com/gabriel-roque/gateway-api/pkg/env"
	"github.com/gin-gonic/gin"
	"go.elastic.co/apm/module/apmgin/v2"
)

func main() {
	env.LoadEnvs()
	if err := database.Migrate(); err != nil {
		log.Fatalf("database migration failed: %v", err)
	}
	defer database.CloseConnection()
	router := gin.Default()
	router.Use(apmgin.Middleware(router))

	router.GET("/healthy", func(c *gin.Context) {
		ping := database.DatabasePing()

		c.JSON(200, gin.H{
			"api":      true,
			"database": ping,
		})
	})

	TransactionPixRoutes(router)

	router.Run(":3000")
}
